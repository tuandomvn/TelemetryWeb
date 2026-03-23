namespace TelemetryWeb.Telemetry;

public sealed class TelemetryIngestRequest
{
    public string? App { get; set; }
    public string? Level { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}

