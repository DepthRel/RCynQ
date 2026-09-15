namespace RCynQ.Core.Config;

/// <summary>
/// The result of comparing the data directory with the computer it runs on.
/// </summary>
/// <param name="CopiedFromAnotherMachine">Whether the data was created on a different computer.</param>
/// <param name="RecordedMachineName">The computer recorded in the data directory, or <see langword="null"/>.</param>
public sealed record InstallationCheck(bool CopiedFromAnotherMachine, string? RecordedMachineName);
