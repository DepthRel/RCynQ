using System.Net.NetworkInformation;
using FluentAssertions;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Sync.Configuration.Probing;
using Xunit;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="NetworkAddressClassifier"/> and the tunnel detection of <see cref="SystemNetworkInterfaceSource"/>.
/// </summary>
public sealed class NetworkAddressClassifierTests
{
    /// <summary>
    /// Verifies the address classification.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="tunnel">Whether the interface is a tunnel.</param>
    /// <param name="expected">The expected kind.</param>
    [Theory]
    [InlineData("127.0.0.1", false, NetworkAddressKind.Loopback)]
    [InlineData("::1", true, NetworkAddressKind.Loopback)]
    [InlineData("169.254.10.1", true, NetworkAddressKind.LinkLocal)]
    [InlineData("fe80::1", false, NetworkAddressKind.LinkLocal)]
    [InlineData("2001:0:14c9:d700:38f3:79c8:541e:695", true, NetworkAddressKind.LinkLocal)]
    [InlineData("10.8.0.3", true, NetworkAddressKind.Tunnel)]
    [InlineData("10.1.2.3", false, NetworkAddressKind.Private)]
    [InlineData("172.20.0.5", false, NetworkAddressKind.Private)]
    [InlineData("172.32.0.5", false, NetworkAddressKind.Public)]
    [InlineData("192.168.1.10", false, NetworkAddressKind.Private)]
    [InlineData("100.100.1.1", false, NetworkAddressKind.Private)]
    [InlineData("203.0.113.4", false, NetworkAddressKind.Public)]
    [InlineData("fd00::5", false, NetworkAddressKind.Private)]
    [InlineData("2001:db8::5", false, NetworkAddressKind.Public)]
    [InlineData("not-an-ip", false, NetworkAddressKind.LinkLocal)]
    public void Classifies_Addresses(string address, bool tunnel, NetworkAddressKind expected)
        => NetworkAddressClassifier.Classify(address, tunnel).Should().Be(expected);

    /// <summary>
    /// Verifies that reachable tunnel addresses rank first.
    /// </summary>
    [Fact]
    public void Reachable_Tunnel_Ranks_First()
    {
        var addresses = new[]
        {
            new NetworkAddressInfo("203.0.113.4", "eth0", NetworkAddressKind.Public, true),
            new NetworkAddressInfo("10.8.0.3", "awg0", NetworkAddressKind.Tunnel, false),
            new NetworkAddressInfo("192.168.1.10", "eth1", NetworkAddressKind.Private, true),
            new NetworkAddressInfo("10.9.0.3", "wg0", NetworkAddressKind.Tunnel, true)
        };

        addresses.OrderBy(NetworkAddressClassifier.Rank).Select(address => address.Address)
            .Should().Equal("10.9.0.3", "192.168.1.10", "203.0.113.4", "10.8.0.3");
    }

    /// <summary>
    /// Verifies the tunnel interface detection.
    /// </summary>
    /// <param name="type">The interface type.</param>
    /// <param name="name">The interface name.</param>
    /// <param name="description">The interface description.</param>
    /// <param name="expected">Whether it is a tunnel.</param>
    [Theory]
    [InlineData(NetworkInterfaceType.Ethernet, "awg0", "", true)]
    [InlineData(NetworkInterfaceType.Unknown, "Local Area Connection", "AmneziaWG Tunnel", true)]
    [InlineData(NetworkInterfaceType.Tunnel, "isatap", "", true)]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet", "Intel(R) Ethernet Connection", false)]
    [InlineData(NetworkInterfaceType.Wireless80211, "Wi-Fi", "Wireless Adapter", false)]
    public void Detects_Tunnel_Interfaces(NetworkInterfaceType type, string name, string description, bool expected)
        => SystemNetworkInterfaceSource.IsTunnel(type, name, description).Should().Be(expected);
}
