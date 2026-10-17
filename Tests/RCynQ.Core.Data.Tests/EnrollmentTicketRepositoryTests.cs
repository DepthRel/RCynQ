using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Repositories;
using RCynQ.Core.Data.Tests.Support;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Data.Tests;

/// <summary>
/// Integration tests of <see cref="EnrollmentTicketRepository"/> against a migrated SQLite database.
/// </summary>
public sealed class EnrollmentTicketRepositoryTests : IDisposable
{
    /// <summary>The time the tickets are created at.</summary>
    private static readonly DateTimeOffset _now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The test database.</summary>
    private readonly TestDatabase _database = new();

    /// <summary>
    /// Verifies that a ticket is stored as it is and can be used exactly once.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Ticket_Is_Used_Once()
    {
        var repository = new EnrollmentTicketRepository(_database.ContextFactory);
        var ticket = Ticket(_now.AddHours(24));
        await repository.AddAsync(ticket);

        var stored = await repository.GetAsync(ticket.Id);
        stored!.Role.Should().Be(NodeRole.Workstation);
        stored.SecretHash.Should().Equal(ticket.SecretHash);
        stored.ExpiresAtUtc.Should().Be(ticket.ExpiresAtUtc);
        stored.EnrollmentKey.Should().Be(ticket.EnrollmentKey);
        stored.IsOpen(_now).Should().BeTrue();

        (await repository.TryUseAsync(ticket.Id, "workstation-1", _now.AddMinutes(5))).Should().BeTrue();
        (await repository.TryUseAsync(ticket.Id, "intruder-1", _now.AddMinutes(6))).Should().BeFalse("a ticket is used once");

        var used = await repository.GetAsync(ticket.Id);
        used!.UsedBy.Should().Be("workstation-1");
        used.UsedAtUtc.Should().Be(_now.AddMinutes(5));
        used.IsOpen(_now.AddMinutes(10)).Should().BeFalse();
        (await repository.RevokeAsync(ticket.Id, _now.AddMinutes(10))).Should().BeFalse("a used ticket is not revoked");
    }

    /// <summary>
    /// Verifies that an expired or revoked ticket cannot be used, and that the newest ticket comes first.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Expired_And_Revoked_Tickets_Are_Refused()
    {
        var repository = new EnrollmentTicketRepository(_database.ContextFactory);
        var expired = Ticket(_now.AddHours(1));
        var revoked = Ticket(_now.AddHours(24)) with { CreatedAtUtc = _now.AddMinutes(1) };
        await repository.AddAsync(expired);
        await repository.AddAsync(revoked);

        (await repository.TryUseAsync(expired.Id, "workstation-1", _now.AddHours(2))).Should().BeFalse();
        (await repository.RevokeAsync(revoked.Id, _now.AddMinutes(2))).Should().BeTrue();
        (await repository.TryUseAsync(revoked.Id, "workstation-1", _now.AddMinutes(3))).Should().BeFalse();
        (await repository.TryUseAsync(Guid.NewGuid(), "workstation-1", _now)).Should().BeFalse("an unknown ticket is refused");

        (await repository.GetAllAsync()).Select(ticket => ticket.Id).Should().Equal(revoked.Id, expired.Id);
    }

    /// <summary>
    /// Deletes the test database.
    /// </summary>
    public void Dispose() => _database.Dispose();

    /// <summary>
    /// Builds an open ticket for a Workstation.
    /// </summary>
    /// <param name="expiresAtUtc">When it expires.</param>
    /// <returns>The ticket.</returns>
    private static EnrollmentTicket Ticket(DateTimeOffset expiresAtUtc)
        => new(Guid.NewGuid(), NodeRole.Workstation, new byte[] { 1, 2, 3 }, _now, expiresAtUtc, "ssh-ed25519 AAAA rcynq:enroll");
}
