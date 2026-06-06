using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GeneralHostFrontend.Application;
using GeneralHostFrontend.Core.Settings;
using GeneralHostFrontend.Infrastructure.DependencyInjection;
using GeneralHostFrontend.Infrastructure.Settings;
using GeneralHostFrontend.ViewModels;
using GeneralHostFrontend.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GeneralHostFrontend
{
    public partial class App : Avalonia.Application
    {
        private ServiceProvider? _services;
        private MainWindowViewModel? _mainViewModel;

        public override void Initialize()
        {
            StartupTrace.Write("App.Initialize begin.");
            Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
            AvaloniaXamlLoader.Load(this);
            StartupTrace.Write("App.Initialize completed.");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            StartupTrace.Write("OnFrameworkInitializationCompleted begin.");
            var validator = new HostSettingsValidator();
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "Config", "hostsettings.json");
            ISettingsStore<HostSettings> settingsStore = new JsonSettingsStore<HostSettings>(settingsPath, validator);
            StartupTrace.Write("Settings store created.");
            StartupTrace.Write("Settings load begin.");
            settingsStore.LoadAsync().GetAwaiter().GetResult();
            StartupTrace.Write("Settings loaded.");

            _services = CompositionRoot.Build(settingsStore);
            StartupTrace.Write("CompositionRoot built.");

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                StartupTrace.Write("Classic desktop lifetime detected.");
                _mainViewModel = _services.GetRequiredService<MainWindowViewModel>();
                StartupTrace.Write("MainWindowViewModel resolved.");

                var mainWindow = new MainWindow
                {
                    DataContext = _mainViewModel,
                };

                desktop.MainWindow = mainWindow;
                StartupTrace.Write("MainWindow assigned.");

                desktop.Exit += OnDesktopExit;
            }
            else
            {
                StartupTrace.Write($"Unexpected lifetime: {ApplicationLifetime?.GetType().FullName ?? "null"}.");
            }

            base.OnFrameworkInitializationCompleted();
            StartupTrace.Write("OnFrameworkInitializationCompleted completed.");
        }

        private async void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
        {
            await RunWithTimeoutAsync(async () =>
            {
                if (_mainViewModel is not null)
                {
                    await _mainViewModel.DisposeAsync();
                }

                if (_services is not null)
                {
                    await _services.DisposeAsync();
                }

                await Log.CloseAndFlushAsync();
            }, TimeSpan.FromSeconds(2));
        }

        private static void OnUiThreadUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "startup-error.log"), e.Exception.ToString());
                StartupTrace.Write($"UI exception handled: {e.Exception.Message}");
            }
            catch
            {
            }

            e.Handled = true;
        }

        private static async Task RunWithTimeoutAsync(Func<Task> action, TimeSpan timeout)
        {
            var task = action();
            var completed = await Task.WhenAny(task, Task.Delay(timeout));
            if (completed == task)
            {
                await task;
            }
        }
    }

    internal static class StartupTrace
    {
        private static readonly object Sync = new();

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        Path.Combine(AppContext.BaseDirectory, "startup-trace.log"),
                        $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
                }
            }
            catch
            {
            }
        }
    }
}
