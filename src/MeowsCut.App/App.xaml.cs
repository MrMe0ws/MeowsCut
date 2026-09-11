using System.Text;
using System.Windows;
using System.Windows.Threading;
using MeowsCut.App.Composition;
using MeowsCut.App.Localization;
using MeowsCut.App.Services;
using MeowsCut.App.Theming;
using MeowsCut.App.ViewModels;
using MeowsCut.App.Views;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace MeowsCut.App;

/// <summary>
/// Точка входа: поднимает хост с DI и логированием, показывает окно и запускает
/// фоновую проверку FFmpeg — окно её не ждёт.
/// </summary>
public partial class App : Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private IHost? _host;

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

        // Настройки читаем до показа окна: и язык, и тема нужны раньше первой отрисовки,
        // а до загрузки хранилище отдаёт значения по умолчанию.
        var settings = _host.Services.GetRequiredService<IAppSettingsStore>();
        var current = await settings.LoadAsync(_shutdown.Token);

        LocalizationManager.UseCulture(current.Language);
        _host.Services.GetRequiredService<ThemeManager>().Apply(current.Theme);

        var window = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow = window;
        window.Show();

        // Папки задач, оставшиеся после аварийного завершения, убираем в фоне.
        var tempFactory = _host.Services.GetRequiredService<ITempWorkspaceFactory>();
        _ = Task.Run(() => tempFactory.CleanOrphans(TimeSpan.FromHours(24)), _shutdown.Token);

        var shell = _host.Services.GetRequiredService<ShellViewModel>();
        await shell.InitializeAsync(CommandLine.FindFilePath(e.Args), _shutdown.Token);

        await TrySaveSnapshotAndExitAsync(e.Args, window);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            // Сначала отменяем работающие задачи и ждём, пока ffmpeg завершится
            // и уберёт за собой временные файлы, и только потом гасим хост.
            var jobs = _host.Services.GetRequiredService<IJobQueue>();
            await jobs.ShutdownAsync();
        }

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

    /// <summary>
    /// Режим разработки: нарисовать окно в PNG и закрыться. Позволяет проверять интерфейс
    /// без захвата экрана — в кадр попадает только само приложение.
    /// </summary>
    private async Task TrySaveSnapshotAndExitAsync(string[] args, Window window)
    {
        var path = WindowSnapshot.GetRequestedPath(args);
        if (path is null)
        {
            return;
        }

        // Даём макету отработать и подгрузиться кадрам: иначе снимок ловит
        // незавершённую отрисовку и пустой предпросмотр.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await Task.Delay(TimeSpan.FromMilliseconds(1500));

        WindowSnapshot.Save(window, path);
        Shutdown();
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
