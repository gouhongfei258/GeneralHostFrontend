using GeneralHostFrontend.Core.Logging;

namespace GeneralHostFrontend.ViewModels.Dashboard;

public sealed record LogEntryViewModel(
    string Time,
    HostLogLevel Level,
    string Source,
    string Message)
{
    public static LogEntryViewModel From(LogEntry entry)
        => new(entry.Timestamp.ToString("HH:mm:ss.fff"), entry.Level, entry.Source, entry.Exception is null ? entry.Message : $"{entry.Message} {entry.Exception.Message}");
}
