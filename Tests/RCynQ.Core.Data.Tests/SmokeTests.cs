using FluentAssertions;
using RCynQ.Core.Enums;
using Xunit;

namespace RCynQ.Core.Data.Tests;

/// <summary>
/// Smoke tests for the RCynQ.Core.Data test project.
/// </summary>
public class SmokeTests
{
    /// <summary>
    /// Verifies that the Core contracts resolve through RCynQ.Core.Data.
    /// </summary>
    [Fact]
    public void Can_Resolve_Core_Contracts()
    {
        NodeRole.Source.Should().BeDefined();
    }
}
