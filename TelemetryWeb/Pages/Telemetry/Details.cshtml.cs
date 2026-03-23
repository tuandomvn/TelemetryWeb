using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryWeb.Telemetry;

namespace TelemetryWeb.Pages.Telemetry
{
    public class DetailsModel : PageModel
    {
        private readonly TelemetryStore _store;

        public TelemetryEntry? Entry { get; private set; }

        public DetailsModel(TelemetryStore store)
        {
            _store = store;
        }

        public IActionResult OnGet(string id, DateTimeOffset? timestamp)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest("id is required.");
            }

            Entry = _store.GetById(id, timestamp);
            if (Entry is null)
            {
                return NotFound();
            }

            return Page();
        }
    }
}

