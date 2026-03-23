using TelemetryWeb.Telemetry;

namespace TelemetryWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddSingleton<TelemetryStore>(_ =>
            {
                var telemetryDataFolder = builder.Configuration["Telemetry:DataFolder"];
                var telemetryDbFileNameTemplate = builder.Configuration["Telemetry:DbFileNameTemplate"];
                var maxEntriesValue = builder.Configuration["Telemetry:MaxEntries"];
                var maxMonthsToScanValue = builder.Configuration["Telemetry:MaxMonthsToScan"];

                var dataFolder = string.IsNullOrWhiteSpace(telemetryDataFolder) ? "App_Data" : telemetryDataFolder!.Trim();
                var dbFileNameTemplate = string.IsNullOrWhiteSpace(telemetryDbFileNameTemplate)
                    ? "telemetry-{yyyyMM}.litedb"
                    : telemetryDbFileNameTemplate!.Trim();

                var maxEntries = 50_000;
                if (!string.IsNullOrWhiteSpace(maxEntriesValue) && int.TryParse(maxEntriesValue, out var parsed))
                {
                    maxEntries = parsed;
                }

                var maxMonthsToScan = 24;
                if (!string.IsNullOrWhiteSpace(maxMonthsToScanValue) && int.TryParse(maxMonthsToScanValue, out var parsedMonths))
                {
                    maxMonthsToScan = parsedMonths;
                }

                var resolvedDataFolder = Path.IsPathRooted(dataFolder)
                    ? dataFolder
                    : Path.Combine(builder.Environment.ContentRootPath, dataFolder);

                return new TelemetryStore(resolvedDataFolder, dbFileNameTemplate, maxEntries, maxMonthsToScan);
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
                app.UseHttpsRedirection();
            }

            app.UseRouting();
            app.UseAuthorization();

            app.MapStaticAssets();

            app.MapPost("/api/telemetry", (TelemetryIngestRequest req, TelemetryStore store) =>
            {
                if (req is null)
                {
                    return Results.BadRequest("Request body is required.");
                }

                var appId = req.App?.Trim();
                if (string.IsNullOrWhiteSpace(appId))
                {
                    return Results.BadRequest("`app` is required.");
                }

                var message = req.Message?.Trim();
                if (string.IsNullOrWhiteSpace(message))
                {
                    return Results.BadRequest("`message` is required.");
                }

                var entry = new TelemetryEntry(
                    Id: null,
                    Timestamp: req.Timestamp ?? DateTime.Now,
                    App: appId,
                    Level: req.Level,
                    Message: message);

                store.Add(entry);
                return Results.Accepted();
            });

            app.MapGet("/api/telemetry/apps", (TelemetryStore store) =>
            {
                var apps = store.GetAppIds();
                return Results.Ok(apps);
            });

            app.MapGet("/api/telemetry/levels", (TelemetryStore store) =>
            {
                var levels = store.GetLevels();
                return Results.Ok(levels);
            });

            app.MapGet("/api/telemetry", (string? app, int? limit, string? date, string? q, string? level, TelemetryStore store) =>
            {
                var take = limit ?? 200;

                DateOnly? dayUtc = null;
                if (!string.IsNullOrWhiteSpace(date))
                {
                    if (!DateOnly.TryParse(date, out var parsedDay))
                    {
                        return Results.BadRequest("Invalid `date`. Use format YYYY-MM-DD.");
                    }

                    dayUtc = parsedDay;
                }

                var latest = store.GetLatest(app, take, dayUtc, q, level);
                return Results.Ok(latest);
            });

            app.MapRazorPages()
               .WithStaticAssets();

            app.Run();
        }
    }
}
