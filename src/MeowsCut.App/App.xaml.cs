using System.Text;
using System.Windows;
using System.Windows.Threading;
using MeowsCut.App.Composition;
using MeowsCut.App.Localization;
using MeowsCut.App.ViewModels;
using MeowsCut.App.Views;
using MeowsCut.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace MeowsCut.App;

/// <summary>
/// Точка входа: поднимает хост с DI и логированием, показывает окно и запускает
/// фоновую проверку FFmpeg — окно не ждёт её.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private readonly CancellationTokenSource _shutdown = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();
        paths.EnsureCreated();

        ConfigureLogging(paths);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: true);

        builder.Services.AddMeowsCutCore();
        builder.Services.AddMeowsCutFfmpeg();
        builder.Services.AddMeowsCutUi();

        _host = builder.Build();
        await _host.StartAsync(_shutdown.Token);

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Meows Cut запущен");

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var settings = _host.Services.GetRequiredService<IAppSettingsStore>();
        LocalizationManager.UseCulture(settings.Current.Language);

        var window = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow = window;
        window.Show();

        var shell = _host.Services.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync(_shutdown.Token);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        _shutdown.Dispose();

        base.OnExit(e);
    }

    private static void ConfigureLogging(AppPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                System.IO.Path.Combine(paths.LogsDirectory, "meowscut-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                restrictedToMinimumLevel: LogEventLevel.Debug,
                // BOM обязателен: без него Блокнот и PowerShell читают русские логи как кракозябры
                encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                shared: true)
            .CreateLogger();
    }

    /// <summary>
    /// Необработанное исключение в UI-потоке не должно закрывать приложение молча.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Необработанное исключение в UI-потоке");

        MessageBox.Show(
            e.Exception.Message,
            LocalizationManager.Get("ErrorTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
