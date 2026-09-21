using System.IO.Compression;
using System.Text;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Commands;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Patches;
using RCynQ.Git.Patches.Bundles;
using RCynQ.Git.Patches.Diff;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Git.Paths;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests for the git command runner, diff builder, bundle builder, patch builder and serializer.
/// </summary>
public class PatchBuildingTests
{
    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly PathUtility _paths = new();

    /// <summary>
    /// Verifies that git commands get byte-exact configuration and a non-interactive environment.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task GitCommandRunner_Adds_Safe_Configuration()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(new ProcessResult(0, "", ""));
        var git = new GitCommandRunner(runner, new GitOptions { GitExecutable = "/usr/bin/git" });

        await git.RunAsync(new[] { "status" }, "/work", new Dictionary<string, string> { ["GIT_SSH_COMMAND"] = "ssh -J a@b:22" });

        await runner.Received(1).RunAsync(
            Arg.Is<ProcessSpec>(spec =>
                spec.FileName == "/usr/bin/git"
                && spec.Arguments.SequenceEqual(GitCommandRunner.ConfigPrefix.Append("status"))
                && spec.WorkingDirectory == "/work"
                && spec.EnvironmentVariables!["GIT_TERMINAL_PROMPT"] == "0"
                && spec.EnvironmentVariables["GIT_ALLOW_PROTOCOL"] == "ssh:file"
                && spec.EnvironmentVariables["GIT_SSH_COMMAND"] == "ssh -J a@b:22"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies diff builder exit code handling.
    /// </summary>
    /// <param name="exitCode">The simulated exit code.</param>
    /// <param name="output">The simulated diff output.</param>
    /// <param name="shouldSucceed">Whether the build should succeed.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(0, "", true)]
    [InlineData(1, "diff --git a/x b/x", true)]
    [InlineData(1, "", false)]
    [InlineData(2, "", false)]
    [InlineData(128, "partial", false)]
    public async Task GitDiffBuilder_Handles_Exit_Codes(int exitCode, string output, bool shouldSucceed)
    {
        using var staging = new TempDirectory();
        var git = Substitute.For<IGitCommandRunner>();
        git.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string?>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var args = call.Arg<IReadOnlyList<string>>();
                var outputFile = args.Single(arg => arg.StartsWith("--output=", StringComparison.Ordinal))["--output=".Length..];
                if (output.Length > 0)
                {
                    File.WriteAllText(outputFile, output);
                }

                return new ProcessResult(exitCode, "", "error: something");
            });

        var build = () => new GitDiffBuilder(git).BuildAsync(staging.Path);

        if (shouldSucceed)
        {
            (await build()).Should().Equal(Encoding.UTF8.GetBytes(output));
        }
        else
        {
            await build.Should().ThrowAsync<GitExecutionException>();
        }

        await git.Received(1).RunAsync(
            Arg.Is<IReadOnlyList<string>>(args => args.Take(3).SequenceEqual(new[] { "diff", "--no-index", "--binary" }) && args.TakeLast(3).SequenceEqual(new[] { "--", "a", "b" })),
            Path.GetFullPath(staging.Path),
            Arg.Any<IReadOnlyDictionary<string, string>?>(),
            Arg.Any<CancellationToken>());
        Directory.Exists(Path.Combine(staging.Path, "a")).Should().BeTrue();
    }

    /// <summary>
    /// Verifies bundle round-trip with deletion markers and manifest verification.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Bundle_RoundTrip_And_Verification()
    {
        using var incoming = new TempDirectory();
        incoming.Write("src/a.txt", "alpha");
        incoming.Write("b.bin", "");
        var manifest = await new DefaultFileManifestBuilder(new SnapshotFilter()).BuildAsync(Path.Combine(incoming.Path, "none"), incoming.Path);
        manifest = manifest.Append(new FileChange(ChangeKind.Deleted, "old/c.txt", null, null, false)).ToArray();

        var builder = new FileBundleBuilder(_paths);
        var bytes = await builder.BuildAsync(manifest, incoming.Path);
        var bundle = builder.Read(bytes, manifest);

        Encoding.UTF8.GetString(bundle.Files["src/a.txt"]).Should().Be("alpha");
        bundle.Deleted.Should().BeEquivalentTo(new[] { "old/c.txt" });

        var wrongManifest = manifest.Select(change => change.Path == "src/a.txt"
            ? new FileChange(ChangeKind.Modified, change.Path, new string('0', 64), 5, false)
            : change).ToArray();
        var verifyWrong = () => builder.Read(bytes, wrongManifest);
        verifyWrong.Should().Throw<FormatException>();

        incoming.Write("src/a.txt", "changed after scan");
        var rebuild = () => builder.BuildAsync(manifest, incoming.Path);
        await rebuild.Should().ThrowAsync<SyncBridgeException>();
    }

    /// <summary>
    /// Verifies that bundles with zip-slip or Git metadata entries are rejected.
    /// </summary>
    /// <param name="entryName">The malicious entry name.</param>
    [Theory]
    [InlineData("files/../evil.txt")]
    [InlineData("files/.git/hooks/post-checkout")]
    [InlineData("files//abs")]
    [InlineData("unknown/x")]
    public void Bundle_Read_Rejects_Unsafe_Entries(string entryName)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry(entryName).Open();
            stream.Write("x"u8);
        }

        var read = () => new FileBundleBuilder(_paths).Read(buffer.ToArray());
        read.Should().Throw<FormatException>();
    }

    /// <summary>
    /// Verifies that the patch builder stages base and incoming versions and fills the envelope.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchBuilder_Stages_Files_And_Builds_Envelope()
    {
        using var baseDir = new TempDirectory();
        using var work = new TempDirectory();
        baseDir.Write("mod.txt", "old");
        baseDir.Write("del.txt", "bye");
        work.Write("mod.txt", "new");
        work.Write("add/x.txt", "added");

        var staged = new Dictionary<string, string>();
        var diffBuilder = Substitute.For<IGitDiffBuilder>();
        diffBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var root = call.Arg<string>();
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                staged[Path.GetRelativePath(root, file).Replace('\\', '/')] = File.ReadAllText(file);
            }

            return new byte[] { 42 };
        });

        var manifestBuilder = new DefaultFileManifestBuilder(new SnapshotFilter());
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(now);
        var builder = new DefaultPatchBuilder(manifestBuilder, new DefaultStateHasher(), diffBuilder, new FileBundleBuilder(_paths), _paths, clock);

        var envelope = await builder.BuildAsync(new NodeId("source"), new NodeId("dest"), baseDir.Path, work.Path);

        staged.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["a/mod.txt"] = "old",
            ["a/del.txt"] = "bye",
            ["b/mod.txt"] = "new",
            ["b/add/x.txt"] = "added"
        });
        envelope.Diff.Should().Equal(42);
        envelope.Manifest.Should().HaveCount(3);
        envelope.CreatedAtUtc.Should().Be(now);
        envelope.BaseStateHash.Should().Be(new DefaultStateHasher().ComputeHash(await manifestBuilder.ScanAsync(baseDir.Path)));
        envelope.IncomingStateHash.Should().NotBe(envelope.BaseStateHash);
        new FileBundleBuilder(_paths).Read(envelope.Bundle, envelope.Manifest).Count.Should().Be(3);
    }

    /// <summary>
    /// Verifies that no diff is built for identical directories.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchBuilder_Skips_Diff_For_No_Changes()
    {
        using var dir = new TempDirectory();
        dir.Write("a.txt", "same");
        var diffBuilder = Substitute.For<IGitDiffBuilder>();
        var builder = new DefaultPatchBuilder(new DefaultFileManifestBuilder(new SnapshotFilter()), new DefaultStateHasher(), diffBuilder, new FileBundleBuilder(_paths), _paths, TimeProvider.System);

        var envelope = await builder.BuildAsync(new NodeId("s"), new NodeId("d"), dir.Path, dir.Path);

        envelope.Manifest.Should().BeEmpty();
        envelope.Diff.Should().BeEmpty();
        await diffBuilder.DidNotReceiveWithAnyArgs().BuildAsync(default!, default);
    }

    /// <summary>
    /// Verifies serializer round-trip and rejection of unsafe or malformed input.
    /// </summary>
    [Fact]
    public void Serializer_RoundTrip_And_Validation()
    {
        var serializer = new PatchEnvelopeSerializer(_paths);
        var hash = new string('c', 64);
        var envelope = new PatchEnvelope(
            Guid.NewGuid(), new NodeId("source"), new NodeId("dest"), DateTimeOffset.UtcNow, hash, hash,
            new[] { new FileChange(ChangeKind.Modified, "src/a.cs", hash, 3, false), new FileChange(ChangeKind.Deleted, "b.txt", null, null, false) },
            new byte[] { 1, 2 }, new byte[] { 3 }, new byte[] { 9, 9 });

        var json = serializer.Serialize(envelope);
        Encoding.UTF8.GetString(json).Should().Contain("\"unifiedDiffBase64\":\"AQI=\"").And.Contain("\"kind\":\"Modified\"");

        var back = serializer.Deserialize(json);
        back.Id.Should().Be(envelope.Id);
        back.Manifest.Should().BeEquivalentTo(envelope.Manifest);
        back.Diff.Should().Equal(1, 2);
        back.Signature.Should().Equal(9, 9);

        var traversal = serializer.ToDto(envelope);
        traversal.Changes[0].Path = "../outside.txt";
        var fromTraversal = () => serializer.FromDto(traversal);
        fromTraversal.Should().Throw<FormatException>();

        var badBase64 = serializer.ToDto(envelope);
        badBase64.UnifiedDiffBase64 = "!!!";
        var fromBadBase64 = () => serializer.FromDto(badBase64);
        fromBadBase64.Should().Throw<FormatException>();

        var malformed = () => serializer.Deserialize("{not json"u8);
        malformed.Should().Throw<FormatException>();
    }
}
