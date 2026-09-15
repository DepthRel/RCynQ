namespace RCynQ.Service.Hosting;

/// <summary>
/// Facts about the running service process.
/// </summary>
/// <param name="ConfigFilePath">The configuration file the service was started with.</param>
public sealed record ServiceRuntimeInfo(string ConfigFilePath);
