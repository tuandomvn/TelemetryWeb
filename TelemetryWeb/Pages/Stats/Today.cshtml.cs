using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryWeb.Telemetry;

namespace TelemetryWeb.Pages.Stats
{
    public class TodayModel : PageModel
    {
        private readonly TelemetryStore _store;

        public DateOnly TodayUtc { get; private set; }
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

            TodayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
            Items = _store.GetTodayAppSummaries(TodayUtc, IdleMinutes);
            IdleCount = Items.Count(x => x.IsIdle);
        }
    }
}

