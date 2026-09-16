using System.Text.Json;
using ScreenBux.Shared.Utilities;

namespace ScreenBux.Service.Services;

/// <summary>
/// Writes a local, file-based supplementary log of foreground activity (time/category/process/
/// window title), one JSON-Lines file per effective day (see
/// <see cref="UsageTrackingService.ComputeEffectiveDate"/>), so a human-readable trail of "what
/// was in the foreground when" exists without adding per-title write volume to the server's
/// SQL Server usage tables - those stay aggregated per-category totals only (see
/// <see cref="UsageTrackingService"/>/<c>EfUsageStore</c>).
///
/// Segments are transition-based, not per-second: <see cref="ReportSegment"/> closes and writes
/// the previous segment only when the foreground category/process/title actually changes (or on
/// day rollover, or on <see cref="Flush"/> at shutdown), keeping file size proportional to how
/// often the user switches windows rather than to elapsed time.
/// </summary>
public class UsageActivityLogWriter
{
    private const int RetentionDays = 14;
    private static readonly JsonSerializerOptions SerializerOptions = new();

    private readonly ILogger<UsageActivityLogWriter> _logger;
    private readonly string _directory;
    private readonly object _lock = new();

    private DateOnly? _openEffectiveDate;
    private DateTime? _openStartedAt;
    private string _openCategoryName = string.Empty;
    private string? _openProcessName;
    private string? _openWindowTitle;

    public UsageActivityLogWriter(ILogger<UsageActivityLogWriter> logger, IConfiguration configuration)
    {
        _logger = logger;
        _directory = configuration["UsageLogDirectory"] ?? PolicyStorage.GetDefaultUsageLogDirectory();
    }

    /// <summary>
    /// Reports the currently foreground category/process/title as of <paramref name="nowLocal"/>.
    /// A no-op if nothing changed since the last call; otherwise closes and appends the previous
    /// segment and opens a new one. Call on every foreground report, same as
    /// <see cref="UsageTrackingService.ReportForegroundCategory"/>.
    /// </summary>
    public void ReportSegment(DateTime nowLocal, int dayStartHour, string categoryName, string? processName, string? windowTitle)
    {
        var effectiveDate = UsageTrackingService.ComputeEffectiveDate(nowLocal, dayStartHour);

        lock (_lock)
        {
            if (_openStartedAt is not null &&
                _openEffectiveDate == effectiveDate &&
                _openCategoryName == categoryName &&
                _openProcessName == processName &&
                _openWindowTitle == windowTitle)
            {
                return;
            }

            CloseOpenSegmentLocked(nowLocal);

            _openEffectiveDate = effectiveDate;
            _openStartedAt = nowLocal;
            _openCategoryName = categoryName;
            _openProcessName = processName;
            _openWindowTitle = windowTitle;
        }
    }

    /// <summary>Closes and writes the currently open segment, if any, without opening a new one.</summary>
    public void Flush(DateTime nowLocal)
    {
        lock (_lock)
        {
            CloseOpenSegmentLocked(nowLocal);
            _openStartedAt = null;
        }
    }

    private void CloseOpenSegmentLocked(DateTime endedAt)
    {
        if (_openStartedAt is not DateTime startedAt || _openEffectiveDate is not DateOnly effectiveDate)
        {
            return;
        }

        var durationSeconds = (long)(endedAt - startedAt).TotalSeconds;
        if (durationSeconds <= 0)
        {
            return;
        }

        var entry = new UsageActivityLogEntry
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            DurationSeconds = durationSeconds,
            CategoryName = _openCategoryName,
            ProcessName = _openProcessName,
            WindowTitle = _openWindowTitle
        };

        AppendEntry(effectiveDate, entry);
    }

    private void AppendEntry(DateOnly effectiveDate, UsageActivityLogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            var line = JsonSerializer.Serialize(entry, SerializerOptions);
            File.AppendAllText(GetLogFilePath(effectiveDate), line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append usage activity log entry for {EffectiveDate}.", effectiveDate);
        }
    }

    private string GetLogFilePath(DateOnly effectiveDate) =>
        Path.Combine(_directory, $"usage-{effectiveDate:yyyyMMdd}.jsonl");

    /// <summary>
    /// Reads every logged segment for one effective day, oldest first. Returns an empty list if
    /// that day has no log file (yet, or never had any activity). The one hook a future
    /// parse/analyze iteration would build on.
    /// </summary>
    public IReadOnlyList<UsageActivityLogEntry> ReadDay(DateOnly effectiveDate)
    {
        var path = GetLogFilePath(effectiveDate);
        if (!File.Exists(path))
        {
            return Array.Empty<UsageActivityLogEntry>();
        }

        var entries = new List<UsageActivityLogEntry>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<UsageActivityLogEntry>(line, SerializerOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping malformed usage activity log line in {Path}.", path);
            }
        }

        return entries;
    }

    /// <summary>
    /// Deletes day-log files older than <see cref="RetentionDays"/>, so local disk usage stays
    /// bounded. Cheap enough to call once per day rollover (see <see cref="UsageTrackingService"/>).
    /// </summary>
    public void PruneOldLogs(DateOnly today)
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            var cutoff = today.AddDays(-RetentionDays);
            foreach (var path in Directory.EnumerateFiles(_directory, "usage-????????.jsonl"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (DateOnly.TryParseExact(name.AsSpan("usage-".Length), "yyyyMMdd", out var fileDate) && fileDate < cutoff)
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prune old usage activity logs.");
        }
    }
}
