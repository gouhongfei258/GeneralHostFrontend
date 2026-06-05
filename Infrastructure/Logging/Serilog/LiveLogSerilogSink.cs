using GeneralHostFrontend.Core.Logging;
using Serilog.Core;
using Serilog.Events;

namespace GeneralHostFrontend.Infrastructure.Logging.Serilog;

public sealed class LiveLogSerilogSink : ILogEventSink
{
    private readonly ILiveLogService _liveLogs;

    public LiveLogSerilogSink(ILiveLogService liveLogs)
    {
        _liveLogs = liveLogs;
    }

    public void Emit(LogEvent logEvent)
    {
        _liveLogs.Write(new LogEntry(
            logEvent.Timestamp,
            ToHostLevel(logEvent.Level),
            ResolveSource(logEvent),
            logEvent.RenderMessage(),
            logEvent.Exception));
    }

    private static HostLogLevel ToHostLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => HostLogLevel.Trace,
            LogEventLevel.Debug => HostLogLevel.Debug,
            LogEventLevel.Information => HostLogLevel.Information,
            LogEventLevel.Warning => HostLogLevel.Warning,
            LogEventLevel.Error => HostLogLevel.Error,
            LogEventLevel.Fatal => HostLogLevel.Critical,
            _ => HostLogLevel.Information
        };
    }

    private static string ResolveSource(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out var source))
        {
            return source.ToString().Trim('"');
        }

        return "System";
    }
}
