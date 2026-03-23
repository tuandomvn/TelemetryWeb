namespace TelemetryWeb.Telemetry;

public sealed record AppTodaySummary(
    string App,
    int LogsToday,
    DateTime? LastLogUtc,
    bool IsIdle);

