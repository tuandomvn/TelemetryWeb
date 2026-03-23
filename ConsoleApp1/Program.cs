using System.Net.Http;
using System.Net.Http.Json;

namespace ConsoleApp1
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5063";
            var endpoint = $"{baseUrl}/api/telemetry";

            using var http = new HttpClient();

            var random = new Random();
            var messages = new[]
            {
                "User authentication successful for {0}",
                "Database query execution time: {0}ms",
                "Cache miss for key: {0}-index-alpha",
                "Background worker {0} started processing queue",
                "Unexpected null reference at Module_{0}.Initialize",
                "Heartbeat signal received from Node-{0}"
            };

            for (var i = 0; i < 300; i++)
            {
                var payload = new
                {
                    app = "ConsoleApp1",
                    level = i % 5 == 0 ? "Warning" : "Info",
                    message = string.Format(messages[random.Next(messages.Length)], i),
                    timestamp = DateTimeOffset.UtcNow
                };

                var res = await http.PostAsJsonAsync(endpoint, payload);
                Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] Sent #{i} -> {(int)res.StatusCode}");

                await Task.Delay(1000);
            }
        }
    }
}
