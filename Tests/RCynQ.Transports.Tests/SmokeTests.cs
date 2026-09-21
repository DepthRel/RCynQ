using FluentAssertions;
using RCynQ.Core.Enums;

namespace RCynQ.Transports.Tests;

/// <summary>
/// Smoke tests for the transport test project.
/// </summary>
public class SmokeTests
{
    /// <summary>
    /// Verifies that the Core contracts resolve through the transport projects.
    /// </summary>
    [Fact]
    public void Can_Resolve_Core_Contracts()
    {
        NodeRole.Source.Should().BeDefined();
    }
}
