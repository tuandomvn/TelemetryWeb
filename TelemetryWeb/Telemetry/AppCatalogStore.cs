using LiteDB;

namespace TelemetryWeb.Telemetry;

public sealed class AppCatalogStore : IDisposable
{
    private readonly object _gate = new();
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<AppCatalogDocument> _collection;

    public AppCatalogStore(string dbFilePath)
    {
        if (string.IsNullOrWhiteSpace(dbFilePath))
        {
            throw new ArgumentException("dbFilePath is required.", nameof(dbFilePath));
        }

        var path = dbFilePath.Trim();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _db = new LiteDatabase(path);
        _collection = _db.GetCollection<AppCatalogDocument>("apps");
        _collection.EnsureIndex(x => x.AppName, unique: true);
        _collection.EnsureIndex(x => x.LastSeenUtc);
    }

    public void UpsertApp(string appName, DateTimeOffset seenAtUtc)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return;
        }

        var normalized = appName.Trim();
        lock (_gate)
        {
            var existing = _collection.FindOne(x => x.AppName == normalized);
            if (existing is null)
            {
                _collection.Insert(new AppCatalogDocument
                {
                    AppName = normalized,
                    FirstSeenUtc = seenAtUtc.UtcDateTime,
                    LastSeenUtc = seenAtUtc.UtcDateTime
                });
                return;
            }

            if (seenAtUtc.UtcDateTime > existing.LastSeenUtc)
            {
                existing.LastSeenUtc = seenAtUtc.UtcDateTime;
                _collection.Update(existing);
            }
        }
    }

    public IReadOnlyList<string> GetAppIds()
    {
        lock (_gate)
        {
            return _collection.FindAll()
                .Select(x => x.AppName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private sealed class AppCatalogDocument
    {
        [BsonId]
        public string AppName { get; set; } = string.Empty;

        public DateTime FirstSeenUtc { get; set; }

        public DateTime LastSeenUtc { get; set; }
    }
}
