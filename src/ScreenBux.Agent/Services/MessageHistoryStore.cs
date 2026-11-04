using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenBux.Shared.Messages;

namespace ScreenBux.Agent.Services;

/// <summary>One shown notification, as kept in the Agent's message history.</summary>
public class MessageHistoryEntry
{
    public DateTime ReceivedAt { get; set; }
    public NotificationSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    [JsonIgnore]
    public string When => ReceivedAt.Date == DateTime.Today ? ReceivedAt.ToString("HH:mm") : ReceivedAt.ToString("ddd d MMM HH:mm");

    [JsonIgnore]
    public string Kind => Severity switch
    {
        NotificationSeverity.ParentMessage => "Message",
        NotificationSeverity.Warning => "Warning",
        NotificationSeverity.TimeUp => "Time's up",
        _ => "Info"
    };
}

/// <summary>
/// Keeps the most recent notifications (parent messages and the Service's own time warnings)
/// for the Agent's "Messages" tab. Stored per Windows user under %LocalAppData%\ScreenBux, so it
/// survives Agent restarts. Best-effort: I/O failures never affect showing the pop-up.
/// </summary>
public class MessageHistoryStore
{
    private const int MaxEntries = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _lock = new();
    private List<MessageHistoryEntry> _entries;

    public MessageHistoryStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenBux");
        _path = Path.Combine(folder, "messages.json");
        _entries = Load();
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<MessageHistoryEntry> Entries
    {
        get { lock (_lock) { return _entries.ToList(); } }
    }

    public MessageHistoryEntry Add(PendingNotification notification)
    {
        var entry = new MessageHistoryEntry
        {
            ReceivedAt = DateTime.Now,
            Severity = notification.Severity,
            Title = notification.Title,
            Message = notification.Message
        };

        lock (_lock)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            }

            Save();
        }

        return entry;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            Save();
        }
    }

    private List<MessageHistoryEntry> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<List<MessageHistoryEntry>>(File.ReadAllText(_path), JsonOptions) ?? new();
            }
        }
        catch
        {
            // Corrupt/unreadable history - start fresh.
        }

        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOptions));
        }
        catch
        {
            // Best-effort.
        }
    }
}
