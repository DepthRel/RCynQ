using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.ViewModels.Dialogs;
using Xunit;

namespace RCynQ.UI.Tests.Dialogs;

/// <summary>
/// Tests of the automatic detection in <see cref="ExportDialogViewModel"/>.
/// </summary>
public sealed class ExportDialogViewModelTests
{
    /// <summary>A host key fingerprint.</summary>
    private const string Fingerprint = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";

    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>
    /// Verifies that the probe report fills the form and selects the suggested address.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Probe_Prefills_Form()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:dev"));
        var dialog = new ExportDialogViewModel(_api);

        await dialog.ProbeCommand.ExecuteAsync(null);

        dialog.Addresses.Select(option => option.Address).Should().Equal("10.8.0.3", "192.168.1.10");
        dialog.SelectedAddress!.KindText.Should().Be("VPN / туннель");
        dialog.Host.Should().Be("10.8.0.3");
        dialog.User.Should().Be("dev");
        dialog.SshPort.Should().Be(2222);
        dialog.SshdOk.Should().BeTrue();
        dialog.ServicePortOk.Should().BeFalse();
        dialog.ExportedSshPublicKey.Should().StartWith("ssh-ed25519 ");
        dialog.ExportedHostKeyFingerprint.Should().Be(Fingerprint);
        dialog.Warnings.Should().ContainSingle().Which.Should().Contain("Порт сервиса");
        dialog.CanGenerateKey.Should().BeFalse();

        dialog.IncludeHostKey = false;
        dialog.ExportedHostKeyFingerprint.Should().BeNull();
        dialog.SelectedAddress = dialog.Addresses[1];
        dialog.Host.Should().Be("192.168.1.10");
    }

    /// <summary>
    /// Verifies that a missing key can be generated and the typed user name is kept.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Missing_Key_Can_Be_Generated()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: null));
        _api.GenerateSshKeyAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:dev"));
        var dialog = new ExportDialogViewModel(_api) { User = "rcynq" };

        await dialog.ProbeCommand.ExecuteAsync(null);
        dialog.CanGenerateKey.Should().BeTrue();
        dialog.ExportedSshPublicKey.Should().BeNull();

        await dialog.GenerateKeyCommand.ExecuteAsync(null);

        dialog.HasSshKey.Should().BeTrue();
        dialog.IncludeSshKey.Should().BeTrue();
        dialog.User.Should().Be("rcynq");
    }

    /// <summary>
    /// Verifies that an unreachable service is reported and the export still needs a host.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Probe_Failure_Is_Shown()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns<NodeEnvironmentReport>(_ => throw new HttpRequestException("refused"));
        var dialog = new ExportDialogViewModel(_api);

        await dialog.ProbeCommand.ExecuteAsync(null);
        dialog.ConfirmCommand.Execute(null);

        dialog.HasProbeError.Should().BeTrue();
        dialog.IsProbing.Should().BeFalse();
        dialog.Confirmed.Should().BeFalse();
        dialog.Error.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that a node without an SSH server shows its port and certificate instead of sshd details.
    /// </summary>
    [Fact]
    public void Node_Without_Ssh_Server_Shows_Tls_Details()
    {
        const string Tls = "SHA256:59CE0A214994CBC386DF73DA0F07AF6B19138641408A65ED2330C7FEA1BBFDF4";
        var dialog = new ExportDialogViewModel(_api);

        dialog.Apply(Report(sshKey: null) with
        {
            SshdListening = false,
            HostKeyFingerprints = Array.Empty<string>(),
            EndpointMode = EndpointMode.Direct,
            EndpointPort = 18444,
            TlsFingerprint = Tls
        });

        dialog.IsDirectEndpoint.Should().BeTrue();
        dialog.IsSshEndpoint.Should().BeFalse("the sshd host key section is hidden");
        dialog.TlsFingerprint.Should().Be(Tls);
        dialog.SshdOk.Should().BeTrue("the TLS port answers on 10.8.0.3");
        dialog.SshdStatus.Should().Contain("18444").And.Contain("без SSH-сервера");
        dialog.Addresses[0].StatusText.Should().Be("порт RCynQ отвечает");
        dialog.Addresses[1].StatusText.Should().Be("порт RCynQ не отвечает");
    }

    /// <summary>
    /// Creates a probe report.
    /// </summary>
    /// <param name="sshKey">The SSH public key line, or <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    private static NodeEnvironmentReport Report(string? sshKey)
        => new(
            "DEV-PC",
            "dev",
            2222,
            true,
            false,
            new[]
            {
                new NetworkAddressInfo("10.8.0.3", "awg0", NetworkAddressKind.Tunnel, true),
                new NetworkAddressInfo("192.168.1.10", "Ethernet", NetworkAddressKind.Private, false)
            },
            "10.8.0.3",
            sshKey,
            sshKey is null ? null : "/home/dev/.ssh/rcynq_ed25519.pub",
            sshKey is null ? null : "SHA256:key",
            new[] { Fingerprint },
            new[] { EnvironmentWarning.ServicePortNotListening },
            DateTimeOffset.UtcNow);
}
