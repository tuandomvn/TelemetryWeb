namespace TelemetryWeb.Telemetry;

public sealed record AppTodaySummary(
    string App,
    int LogsToday,
    DateTimeOffset? LastLogUtc,
    bool IsIdle);

