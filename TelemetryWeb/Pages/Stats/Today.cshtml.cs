using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryWeb.Telemetry;

namespace TelemetryWeb.Pages.Stats
{
    public class TodayModel : PageModel
    {
        private readonly TelemetryStore _store;

        public DateOnly Today { get; private set; }
        public int IdleMinutes { get; private set; } = 15;
        public int IdleCount { get; private set; }

        public IReadOnlyList<AppTodaySummary> Items { get; private set; } = Array.Empty<AppTodaySummary>();

        public TodayModel(TelemetryStore store)
        {
            _store = store;
        }

        public void OnGet(int? idleMinutes)
        {
            IdleMinutes = idleMinutes.HasValue && idleMinutes.Value > 0 ? idleMinutes.Value : 15;

            Today = DateOnly.FromDateTime(DateTime.UtcNow);
            Items = _store.GetTodayAppSummaries(Today, IdleMinutes);
            IdleCount = Items.Count(x => x.IsIdle);
        }
    }
}

