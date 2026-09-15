using FluentAssertions;
using RCynQ.UI.Services.Hosting;
using Xunit;

namespace RCynQ.UI.Tests.Hosting;

/// <summary>
/// Tests of <see cref="ServiceExecutableLocator"/>.
/// </summary>
public sealed class ServiceExecutableLocatorTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-locator-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies the published layout: <c>ui/</c> next to <c>service/</c>.
    /// </summary>
    [Fact]
    public void Finds_Published_Service_Next_To_Ui()
    {
        var ui = Directory.CreateDirectory(Path.Combine(_root, "ui")).FullName;
        var exe = Touch(Path.Combine(_root, "service", "RCynQ.Service.exe"));

        var command = ServiceExecutableLocator.Locate(ui + Path.DirectorySeparatorChar, Array.Empty<string>(), _ => null, isWindows: true);

        command.Should().NotBeNull();
        command!.FileName.Should().Be(exe);
        command.PrefixArguments.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the development layout, running the service assembly with <c>dotnet</c> when there is no apphost.
    /// </summary>
    [Fact]
    public void Finds_Development_Build_Of_The_Same_Configuration()
    {
        var ui = Directory.CreateDirectory(Path.Combine(_root, "Apps", "RCynQ.UI", "bin", "Debug", "net8.0")).FullName;
        var dll = Touch(Path.Combine(_root, "Apps", "RCynQ.Service", "bin", "Debug", "net8.0", "RCynQ.Service.dll"));
        Touch(Path.Combine(_root, "Apps", "RCynQ.Service", "bin", "Release", "net8.0", "RCynQ.Service"));

        var command = ServiceExecutableLocator.Locate(ui, Array.Empty<string>(), _ => null, isWindows: false);

        command.Should().NotBeNull();
        command!.FileName.Should().Be("dotnet");
        command.PrefixArguments.Should().Equal(dll);
    }

    /// <summary>
    /// Verifies that an explicit path wins and a missing explicit path is not replaced by a search.
    /// </summary>
    [Fact]
    public void Explicit_Path_Wins()
    {
        var ui = Directory.CreateDirectory(Path.Combine(_root, "ui")).FullName;
        Touch(Path.Combine(_root, "service", "RCynQ.Service"));
        var custom = Touch(Path.Combine(_root, "custom", "service-host"));

        ServiceExecutableLocator.Locate(ui, new[] { ServiceExecutableLocator.Argument, custom }, _ => null, isWindows: false)!
            .FileName.Should().Be(custom);
        ServiceExecutableLocator.Locate(ui, Array.Empty<string>(), name => name == ServiceExecutableLocator.EnvironmentVariable ? Path.Combine(_root, "missing") : null, isWindows: false)
            .Should().BeNull();
    }

    /// <summary>
    /// Verifies that nothing is returned when the service does not exist.
    /// </summary>
    [Fact]
    public void Returns_Null_When_Not_Found()
    {
        var ui = Directory.CreateDirectory(Path.Combine(_root, "somewhere")).FullName;

        ServiceExecutableLocator.Locate(ui, Array.Empty<string>(), _ => null, isWindows: true).Should().BeNull();
    }

    /// <summary>
    /// Verifies that the CLI is found in the published <c>cli/</c> directory and in a development build.
    /// </summary>
    [Fact]
    public void Finds_Cli_Published_And_In_Development()
    {
        var ui = Directory.CreateDirectory(Path.Combine(_root, "ui")).FullName;
        var exe = Touch(Path.Combine(_root, "cli", "rcynq.exe"));
        ExecutableLocator.Locate(ui, "rcynq", "cli", "RCynQ.Cli", isWindows: true, _ => null)!.FileName.Should().Be(exe);

        var devUi = Directory.CreateDirectory(Path.Combine(_root, "Apps", "RCynQ.UI", "bin", "Debug", "net8.0")).FullName;
        var dll = Touch(Path.Combine(_root, "Apps", "RCynQ.Cli", "bin", "Debug", "net8.0", "rcynq.dll"));
        var host = Touch(Path.Combine(_root, "dotnet-host"));
        var command = ExecutableLocator.Locate(devUi, "rcynq", "cli", "RCynQ.Cli", isWindows: false, name => name == "DOTNET_HOST_PATH" ? host : null);
        command!.FileName.Should().Be(host);
        command.PrefixArguments.Should().Equal(dll);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Creates an empty file and its directory.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The full path.</returns>
    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return Path.GetFullPath(path);
    }
}
