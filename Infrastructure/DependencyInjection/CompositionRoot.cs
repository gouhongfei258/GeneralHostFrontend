using GeneralHostFrontend.Application;
using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Logging;
using GeneralHostFrontend.Core.Logic;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Settings;
using GeneralHostFrontend.Infrastructure.Communication;
using GeneralHostFrontend.Infrastructure.Database;
using GeneralHostFrontend.Infrastructure.Logic;
using GeneralHostFrontend.Infrastructure.Logging;
using GeneralHostFrontend.Infrastructure.Logging.Serilog;
using GeneralHostFrontend.Infrastructure.Pipelines;
using GeneralHostFrontend.Infrastructure.Settings;
using GeneralHostFrontend.ViewModels;
using GeneralHostFrontend.ViewModels.Database;
using GeneralHostFrontend.ViewModels.Devices;
using GeneralHostFrontend.ViewModels.Logic;
using GeneralHostFrontend.ViewModels.Tags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace GeneralHostFrontend.Infrastructure.DependencyInjection;

public static class CompositionRoot
{
    public static ServiceProvider Build(ISettingsStore<HostSettings> settingsStore)
    {
        var services = new ServiceCollection();
        var liveLogs = new InMemoryLiveLogService();
        ConfigureSerilog(liveLogs);

        services.AddSingleton<ISettingsValidator<HostSettings>, HostSettingsValidator>();
        services.AddSingleton(settingsStore);
        services.AddSingleton<ILiveLogService>(liveLogs);
        services.AddSingleton<ITagDataPipeline>(provider =>
        {
            var settingsStore = provider.GetRequiredService<ISettingsStore<HostSettings>>();
            return new TagDataPipeline(settingsStore.Current.Pipeline);
        });

        services.AddSingleton<ICommunicationDriverFactory, CommunicationDriverFactory>();
        services.AddSingleton<ICommunicationConnectionPool, CommunicationConnectionPool>();
        services.AddSingleton(provider =>
        {
            var databasePath = Path.Combine(AppContext.BaseDirectory, "Data", "host.db");
            return new SqliteDataViewerQueryService(databasePath);
        });
        services.AddSingleton<Core.Database.IDataViewerQueryService>(provider => provider.GetRequiredService<SqliteDataViewerQueryService>());
        services.AddSingleton<Core.Database.IDatabaseHealthMonitor>(provider => provider.GetRequiredService<SqliteDataViewerQueryService>());
        services.AddSingleton<ILogicGraphStore>(_ =>
        {
            var graphPath = Path.Combine(AppContext.BaseDirectory, "Config", "logicgraph.json");
            return new JsonLogicGraphStore(graphPath);
        });
        services.AddSingleton<ILogicCodeGenerator, CSharpLogicCodeGenerator>();
        services.AddSingleton<ILogicCompiler, NatashaLogicCompiler>();
        services.AddSingleton<HostRuntime>();

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        services.AddTransient<DatabaseViewerViewModel>();
        services.AddSingleton<Func<DatabaseViewerViewModel>>(provider => provider.GetRequiredService<DatabaseViewerViewModel>);
        services.AddTransient<DeviceEditorViewModel>();
        services.AddSingleton<Func<DeviceEditorViewModel>>(provider => provider.GetRequiredService<DeviceEditorViewModel>);
        services.AddTransient<LogicEditorViewModel>();
        services.AddSingleton<Func<LogicEditorViewModel>>(provider => provider.GetRequiredService<LogicEditorViewModel>);
        services.AddTransient<TagEditorViewModel>();
        services.AddSingleton<Func<TagEditorViewModel>>(provider => provider.GetRequiredService<TagEditorViewModel>);
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    private static void ConfigureSerilog(ILiveLogService liveLogs)
    {
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "Logs");
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new LiveLogSerilogSink(liveLogs))
            .WriteTo.Async(sink => sink.File(
                Path.Combine(logDirectory, "host-.log"),
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 20 * 1024 * 1024,
                retainedFileCountLimit: 31,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"))
            .CreateLogger();
    }
}
