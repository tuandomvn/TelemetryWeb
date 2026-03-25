namespace TelemetryWeb.Telemetry;

public sealed record TelemetryEntry(
    string? Id,
    DateTime Timestamp,
    string App,
    string? Level,
    string Message);

