using FluentAssertions;
using RCynQ.Core.Enums;

namespace RCynQ.Git.Tests;

/// <summary>
/// Smoke tests for the RCynQ.Git test project.
/// </summary>
public class SmokeTests
{
    /// <summary>
    /// Verifies that the Core contracts resolve through RCynQ.Git.
    /// </summary>
    [Fact]
    public void Can_Resolve_Core_Contracts()
    {
        NodeRole.Source.Should().BeDefined();
    }
}
