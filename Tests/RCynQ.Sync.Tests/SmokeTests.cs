using FluentAssertions;
using RCynQ.Core.Enums;
using Xunit;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Smoke tests for the RCynQ.Sync test project.
/// </summary>
public class SmokeTests
{
    /// <summary>
    /// Verifies that the Core contracts resolve through RCynQ.Sync.
    /// </summary>
    [Fact]
    public void Can_Resolve_Core_Contracts()
    {
        NodeRole.Source.Should().BeDefined();
    }
}
