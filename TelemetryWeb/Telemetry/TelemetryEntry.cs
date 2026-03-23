namespace TelemetryWeb.Telemetry;

public sealed record TelemetryEntry(
    string? Id,
    DateTimeOffset Timestamp,
    string App,
    string? Level,
    string Message);

