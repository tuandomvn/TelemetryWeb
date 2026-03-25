using System.Collections.Concurrent;
using System.Globalization;
using LiteDB;
using Microsoft.AspNetCore.Components.Forms;

namespace TelemetryWeb.Telemetry;

public sealed class TelemetryStore : IDisposable
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, LiteDatabase> _dbCache = new();
    private readonly string _dataFolder;
    private readonly string _dbFileNameTemplate;
    private readonly int _maxEntriesPerDb;
    private readonly int _maxDaysToScan;

    private readonly string _collectionName = "telemetry";
    private readonly string? _templatePrefix;
    private readonly string? _templateSuffix;
    private readonly string _dbFileSuffix = "yyyyMMdd";

    public TelemetryStore(
        string dataFolder,
        string dbFileNameTemplate,
        int maxEntriesPerDb = 50_000,
        int maxDaysToScan = 2)
    {
        if (string.IsNullOrWhiteSpace(dataFolder))
        {
            throw new ArgumentException("dataFolder is required.", nameof(dataFolder));
        }

        if (string.IsNullOrWhiteSpace(dbFileNameTemplate))
        {
            throw new ArgumentException("dbFileNameTemplate is required.", nameof(dbFileNameTemplate));
        }

        _dataFolder = dataFolder.Trim();
        _dbFileNameTemplate = dbFileNameTemplate.Trim();
        _maxEntriesPerDb = maxEntriesPerDb;
        _maxDaysToScan = maxDaysToScan;

        if (!Directory.Exists(_dataFolder))
        {
            Directory.CreateDirectory(_dataFolder);
        }

        var idx = _dbFileNameTemplate.IndexOf(_dbFileSuffix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            _templatePrefix = _dbFileNameTemplate.Substring(0, idx);
            _templateSuffix = _dbFileNameTemplate.Substring(idx + _dbFileSuffix.Length);
        }
        else
        {
            _templatePrefix = _dbFileNameTemplate;
            _templateSuffix = string.Empty;
        }
    }

    public void Add(TelemetryEntry entry)
    {
        if (entry is null) return;

        var monthKey = ToDateKey(entry.Timestamp);

        var db = GetOrCreateDb(monthKey);
        var col = GetCollection(db);

        var doc = new TelemetryDocument
        {
            Timestamp = entry.Timestamp,
            App = entry.App,
            Level = entry.Level,
            Message = entry.Message
        };

        lock (_gate)
        {
            var id = col.Insert(doc);
            doc.Id = id;

            //if (_maxEntriesPerDb > 0)
            //{
            //    var count = col.Count();
            //    if (count > _maxEntriesPerDb)
            //    {
            //        var removeCount = count - _maxEntriesPerDb;
            //        var oldest = col.FindAll()
            //            .OrderBy(x => x.Timestamp)
            //            .Take(removeCount)
            //            .ToList();

            //        foreach (var item in oldest)
            //        {
            //            col.Delete(item.Id);
            //        }
            //    }
            //}
        }
    }

    public IReadOnlyList<string> GetAppIds()
    {
        var apps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monthKey in GetDayKeysToScan((DateOnly?)null))
        {
            var path = GetDbFilePath(monthKey);
            if (!File.Exists(path)) continue;

            var db = GetOrCreateDb(monthKey);
            var col = GetCollection(db);

            lock (_gate)
            {
                foreach (var doc in col.FindAll())
                {
                    if (!string.IsNullOrWhiteSpace(doc.App))
                    {
                        apps.Add(doc.App);
                    }
                }
            }
        }

        return apps
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> GetLevels()
    {
        var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monthKey in GetDayKeysToScan((DateOnly?)null))
        {
            var path = GetDbFilePath(monthKey);
            if (!File.Exists(path)) continue;

            var db = GetOrCreateDb(monthKey);
            var col = GetCollection(db);

            lock (_gate)
            {
                foreach (var doc in col.FindAll())
                {
                    if (!string.IsNullOrWhiteSpace(doc.Level))
                    {
                        levels.Add(doc.Level);
                    }
                }
            }
        }

        return levels
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public TelemetryEntry? GetById(string id, DateTime? timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        ObjectId objectId;
        try
        {
            objectId = new ObjectId(id);
        }
        catch
        {
            return null;
        }

        var candidateDayKeys = GetDayKeysToScan(timestampUtc);
        foreach (var dayKey in candidateDayKeys)
        {
            var path = GetDbFilePath(dayKey);
            if (!File.Exists(path)) continue;

            var db = GetOrCreateDb(dayKey);
            var col = GetCollection(db);
            var doc = col.FindById(objectId);
            if (doc is null) continue;

            return new TelemetryEntry(
                Id: doc.Id.ToString(),
                Timestamp: DateTime.SpecifyKind(doc.Timestamp, DateTimeKind.Utc),
                App: doc.App,
                Level: doc.Level,
                Message: doc.Message);
        }

        return null;
    }

    public IReadOnlyList<TelemetryEntry> GetLatest(
        string? app,
        int limit,
        DateOnly? dayUtc,
        string? searchText,
        string? level)
    {
        if (limit <= 0) limit = 200;

        DateTime? startUtc = null;
        DateTime? endUtc = null;
        if (dayUtc.HasValue)
        {
            startUtc = dayUtc.Value.ToDateTime(TimeOnly.MinValue);
            endUtc = dayUtc.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            startUtc = DateTime.SpecifyKind(startUtc.Value, DateTimeKind.Utc);
            endUtc = DateTime.SpecifyKind(endUtc.Value, DateTimeKind.Utc);
        }

        var q = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
        var lvl = string.IsNullOrWhiteSpace(level) ? null : level.Trim();
        var appFilter = string.IsNullOrWhiteSpace(app) ? null : app.Trim();

        var candidates = new List<TelemetryDocument>();
        foreach (var dayKey in GetDayKeysToScan(dayUtc))
        {
            var path = GetDbFilePath(dayKey);
            if (!File.Exists(path)) continue;

            var db = GetOrCreateDb(dayKey);
            var col = GetCollection(db);

            IEnumerable<TelemetryDocument> monthQuery = col.FindAll();

            // LiteDB LINQ provider is limited; using Where + filtering by simple fields is OK.
            if (!string.IsNullOrWhiteSpace(appFilter))
            {
                monthQuery = monthQuery.Where(x => x.App == appFilter);
            }

            if (!string.IsNullOrWhiteSpace(lvl))
            {
                monthQuery = monthQuery.Where(x => string.Equals(x.Level, lvl, StringComparison.OrdinalIgnoreCase));
            }

            if (startUtc.HasValue && endUtc.HasValue)
            {
                var s = startUtc.Value;
                var e = endUtc.Value;
                monthQuery = monthQuery.Where(x => x.Timestamp >= s && x.Timestamp < e);
            }

            // newest first
            var list = monthQuery
                .OrderByDescending(x => x.Timestamp)
                .Take(limit)
                .ToList();

            candidates.AddRange(list);
        }

        return candidates
            .OrderByDescending(x => x.Timestamp)
            .Take(limit)
            .Select(x => new TelemetryEntry(
                Id: x.Id.ToString(),
                Timestamp: DateTime.SpecifyKind(x.Timestamp, DateTimeKind.Utc),
                App: x.App,
                Level: x.Level,
                Message: TrimMessage(x.Message)))
            .ToList();
    }

    public string TrimMessage(string message, int maxLength = 200)
    {
        if(string.IsNullOrEmpty(message)) return string.Empty;
        return message.Length > maxLength
            ? message.Substring(0, maxLength) + "..." : message;
    }

    public IReadOnlyList<AppTodaySummary> GetTodayAppSummaries(DateOnly todayUtc, int idleMinutes)
    {
        if (idleMinutes <= 0) idleMinutes = 15;

        var nowUtc = DateTime.Now;
        var idleCutoff = nowUtc.AddMinutes(-idleMinutes);

        var dict = new Dictionary<string, (int countToday, DateTime? lastLogUtc)>(StringComparer.OrdinalIgnoreCase);

        // For "Today Summary" we only need logs that happened today.
        // We'll scan only the month file that contains `todayUtc` for performance.
        var startUtc = DateTime.SpecifyKind(todayUtc.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(todayUtc.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        foreach (var monthKey in GetDayKeysToScan(todayUtc))
        {
            var path = GetDbFilePath(monthKey);
            if (!File.Exists(path)) continue;

            var db = GetOrCreateDb(monthKey);
            var col = GetCollection(db);

            lock (_gate)
            {
                var todayQuery = col
                    .Query()
                    .Where(x => x.Timestamp >= startUtc && x.Timestamp < endUtc);

                foreach (var doc in todayQuery.ToList())
                {
                    var app = doc.App;
                    if (string.IsNullOrWhiteSpace(app)) continue;

                    //var tsUtc = DateTime.SpecifyKind(doc.Timestamp, DateTimeKind.Utc);
                    var tsDto = doc.Timestamp;

                    if (!dict.TryGetValue(app, out var value))
                    {
                        dict[app] = (1, tsDto);
                        continue;
                    }

                    var countToday = value.countToday + 1;
                    var lastLogUtc = value.lastLogUtc;
                    if (!lastLogUtc.HasValue || tsDto > lastLogUtc.Value)
                    {
                        lastLogUtc = tsDto;
                    }

                    dict[app] = (countToday, lastLogUtc);
                }
            }
        }

        return dict
            .Select(kvp =>
            {
                var last = kvp.Value.lastLogUtc;
                var isIdle = !last.HasValue || last.Value < idleCutoff;
                return new AppTodaySummary(
                    App: kvp.Key,
                    LogsToday: kvp.Value.countToday,
                    LastLogUtc: last,
                    IsIdle: isIdle);
            })
            .OrderByDescending(x => x.IsIdle)
            .ThenByDescending(x => x.LogsToday)
            .ThenBy(x => x.App, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<string> GetDayKeysToScan(DateOnly? dayUtc)
    {
        if (dayUtc.HasValue)
        {
            var monthKey = dayUtc.Value.ToString(_dbFileSuffix, CultureInfo.InvariantCulture);
            return new[] { monthKey };
        }

        var nowUtc = DateTime.Now;
        var result = new List<string>(_maxDaysToScan);
        for (var i = 0; i < _maxDaysToScan; i++)
        {
            var dt = nowUtc.AddDays(-i);
            result.Add(dt.ToString(_dbFileSuffix, CultureInfo.InvariantCulture));
        }
        return result;
    }

    private IEnumerable<string> GetDayKeysToScan(DateTime? timestampUtc)
    {
        if (timestampUtc.HasValue)
        {
            return new[] { ToDateKey(timestampUtc.Value) };
        }

        // fallback: scan recent months
        return GetDayKeysToScan((DateOnly?)null);
    }

    private string ToDateKey(DateTime timestamp)
        => timestamp.ToString(_dbFileSuffix, CultureInfo.InvariantCulture);

    private string GetDbFilePath(string dayKey)
    {
        var fileName = _dbFileNameTemplate.Contains(_dbFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? _dbFileNameTemplate.Replace(_dbFileSuffix, dayKey, StringComparison.OrdinalIgnoreCase)
            : _dbFileNameTemplate;

        return Path.Combine(_dataFolder, fileName);
    }

    private LiteDatabase GetOrCreateDb(string dayKey)
    {
        var path = GetDbFilePath(dayKey);
        return _dbCache.GetOrAdd(dayKey, _ => new LiteDatabase(path));
    }

    private ILiteCollection<TelemetryDocument> GetCollection(LiteDatabase db)
    {
        var col = db.GetCollection<TelemetryDocument>(_collectionName);
        col.EnsureIndex(x => x.App);
        col.EnsureIndex(x => x.Timestamp);
        col.EnsureIndex(x => x.Level);
        return col;
    }

    public void Dispose()
    {
        foreach (var item in _dbCache)
        {
            try
            {
                item.Value.Dispose();
            }
            catch
            {
                // ignore dispose errors
            }
        }
    }

    private sealed class TelemetryDocument
    {
        [BsonId]
        public ObjectId Id { get; set; } = default!;

        public DateTime Timestamp { get; set; }

        public string App { get; set; } = string.Empty;

        public string? Level { get; set; }

        public string Message { get; set; } = string.Empty;
    }
}

