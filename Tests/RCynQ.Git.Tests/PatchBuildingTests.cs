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
using RCynQ.Git.Patches.Diff;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Git.Paths;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests for the git command runner, diff builder, patch diff builder, patch builder and serializer.
/// </summary>
public class PatchBuildingTests
{
    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly PathUtility _paths = new(TestTexts.English);

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
                spec.FileName == "/usr/bin/git" &&
                spec.Arguments.SequenceEqual(GitCommandRunner.ConfigPrefix.Append("status")) &&
                spec.WorkingDirectory == "/work" &&
                spec.EnvironmentVariables!["GIT_TERMINAL_PROMPT"] == "0" &&
                spec.EnvironmentVariables["GIT_ALLOW_PROTOCOL"] == "ssh:file" &&
                spec.EnvironmentVariables["GIT_SSH_COMMAND"] == "ssh -J a@b:22"),
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
    /// Verifies that the patch builder fills the envelope with the manifest and the state hashes and holds no content.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchBuilder_Builds_Manifest_Envelope()
    {
        using var baseDir = new TempDirectory();
        using var work = new TempDirectory();
        baseDir.Write("mod.txt", "old");
        baseDir.Write("del.txt", "bye");
        work.Write("mod.txt", "new");
        work.Write("add/x.txt", "added");

        var manifestBuilder = new DefaultFileManifestBuilder(new SnapshotFilter());
        var now = new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(now);
        var builder = new DefaultPatchBuilder(manifestBuilder, new DefaultStateHasher(), clock);

        var envelope = await builder.BuildAsync(new NodeId("workstation"), new NodeId("origin"), baseDir.Path, work.Path);

        envelope.Manifest.Select(change => (change.Kind, change.Path)).Should().Equal(
            (ChangeKind.Added, "add/x.txt"), (ChangeKind.Deleted, "del.txt"), (ChangeKind.Modified, "mod.txt"));
        envelope.CreatedAtUtc.Should().Be(now);
        envelope.BaseStateHash.Should().Be(new DefaultStateHasher().ComputeHash(await manifestBuilder.ScanAsync(baseDir.Path)));
        envelope.IncomingStateHash.Should().NotBe(envelope.BaseStateHash);
        envelope.ContentHashes.Should().HaveCount(2, "the deleted file needs no content");
        envelope.IsSigned.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that identical directories produce an empty manifest.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchBuilder_Builds_Empty_Manifest_For_No_Changes()
    {
        using var dir = new TempDirectory();
        dir.Write("a.txt", "same");
        var builder = new DefaultPatchBuilder(new DefaultFileManifestBuilder(new SnapshotFilter()), new DefaultStateHasher(), TimeProvider.System);

        var envelope = await builder.BuildAsync(new NodeId("s"), new NodeId("d"), dir.Path, dir.Path);

        envelope.Manifest.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the diff builder stages the old and the new side of every file and runs the git diff on them.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchDiffBuilder_Stages_Old_And_New_Sides()
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
        var manifest = await new DefaultFileManifestBuilder(new SnapshotFilter()).BuildAsync(baseDir.Path, work.Path);

        var diff = await new PatchDiffBuilder(diffBuilder, _paths).BuildAsync(manifest, baseDir.Path, new DirectoryContent(work.Path), PatchDiffLimits.Full);

        diff.Should().Equal(42);
        staged.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["a/mod.txt"] = "old",
            ["a/del.txt"] = "bye",
            ["b/mod.txt"] = "new",
            ["b/add/x.txt"] = "added"
        });
    }

    /// <summary>
    /// Verifies that a diff built for display leaves large files out and names them, while a full diff includes them.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchDiffBuilder_Display_Limits_Skip_Large_Files_But_Full_Does_Not()
    {
        using var baseDir = new TempDirectory();
        using var work = new TempDirectory();
        work.Write("small.txt", "tiny");
        work.Write("big.txt", new string('x', 5000));
        var staged = new List<string>();
        var diffBuilder = Substitute.For<IGitDiffBuilder>();
        diffBuilder.BuildAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var root = call.Arg<string>();
            staged.Clear();
            staged.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(root, file).Replace('\\', '/')));
            return "diff\n"u8.ToArray();
        });
        var manifest = await new DefaultFileManifestBuilder(new SnapshotFilter()).BuildAsync(baseDir.Path, work.Path);
        var builder = new PatchDiffBuilder(diffBuilder, _paths);

        var display = await builder.BuildAsync(manifest, baseDir.Path, new DirectoryContent(work.Path), new PatchDiffLimits(1000, null));

        staged.Should().Equal("b/small.txt");
        Encoding.UTF8.GetString(display).Should().StartWith("# rcynq: big.txt (5000 bytes) is not shown in this diff.").And.EndWith("diff\n");

        var full = await builder.BuildAsync(manifest, baseDir.Path, new DirectoryContent(work.Path), PatchDiffLimits.Full);

        staged.Should().BeEquivalentTo("b/small.txt", "b/big.txt");
        Encoding.UTF8.GetString(full).Should().Be("diff\n");
    }

    /// <summary>
    /// Verifies serializer round-trip and rejection of unsafe or malformed input.
    /// </summary>
    [Fact]
    public void Serializer_RoundTrip_And_Validation()
    {
        var serializer = new PatchEnvelopeSerializer(TestTexts.English, _paths);
        var hash = new string('c', 64);
        var envelope = new PatchEnvelope(
            Guid.NewGuid(), new NodeId("workstation"), new NodeId("origin"), DateTimeOffset.UtcNow, hash, hash,
            new[]
            {
                new FileChange(ChangeKind.Modified, "src/a.cs", hash, 3, false, new string('d', 64)),
                new FileChange(ChangeKind.Deleted, "b.txt", null, null, false, new string('e', 64)),
                new FileChange(ChangeKind.Added, "c.txt", hash, 1, false)
            },
            new byte[] { 9, 9 });

        var json = serializer.Serialize(envelope);
        var text = Encoding.UTF8.GetString(json);
        text.Should().Contain("\"kind\":\"Modified\"").And.NotContain("Base64\":\"", "no file content is embedded in the manifest");

        var back = serializer.Deserialize(json);
        back.Id.Should().Be(envelope.Id);
        back.Manifest.Should().BeEquivalentTo(envelope.Manifest);
        back.Manifest[0].BaseSha256.Should().Be(new string('d', 64), "the base hash travels with the manifest");

        var addedWithBase = serializer.ToDto(envelope);
        addedWithBase.Changes[2].BaseSha256 = hash;
        serializer.FromDto(addedWithBase).Manifest[2].BaseSha256.Should().BeNull("an added file has no base");
        back.Signature.Should().Equal(9, 9);

        var traversal = serializer.ToDto(envelope);
        traversal.Changes[0].Path = "../outside.txt";
        var fromTraversal = () => serializer.FromDto(traversal);
        fromTraversal.Should().Throw<FormatException>();

        var badSignature = serializer.ToDto(envelope);
        badSignature.Signature = "!!!";
        var fromBadSignature = () => serializer.FromDto(badSignature);
        fromBadSignature.Should().Throw<FormatException>();

        var withoutSize = serializer.ToDto(envelope);
        withoutSize.Changes[0].SizeBytes = null;
        var fromWithoutSize = () => serializer.FromDto(withoutSize);
        fromWithoutSize.Should().Throw<FormatException>("the size bounds the upload of the file");

        var malformed = () => serializer.Deserialize("{not json"u8);
        malformed.Should().Throw<FormatException>();
    }

    /// <summary>
    /// Verifies that a manifest with a <c>null</c> entry, which is valid JSON, is refused as malformed instead of failing
    /// with a null reference.
    /// </summary>
    [Fact]
    public void Serializer_Refuses_A_Null_Manifest_Entry()
    {
        var serializer = new PatchEnvelopeSerializer(TestTexts.English, _paths);
        var hash = new string('c', 64);
        var json = $$"""
            {"patchId":"{{Guid.NewGuid()}}","sender":"workstation","receiver":"origin","createdAt":"2026-01-01T00:00:00+00:00",
             "baseStateHash":"{{hash}}","incomingStateHash":"{{hash}}","changes":[null]}
            """;

        var deserialize = () => serializer.Deserialize(Encoding.UTF8.GetBytes(json));

        deserialize.Should().Throw<FormatException>();
    }
}
