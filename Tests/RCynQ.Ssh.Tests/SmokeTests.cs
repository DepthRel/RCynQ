using FluentAssertions;
using RCynQ.Core.Enums;
using Xunit;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Smoke tests for the RCynQ.Ssh test project.
/// </summary>
public class SmokeTests
{
    /// <summary>
    /// Verifies that the Core contracts resolve through RCynQ.Ssh.
    /// </summary>
    [Fact]
    public void Can_Resolve_Core_Contracts()
    {
        NodeRole.Source.Should().BeDefined();
    }
}
