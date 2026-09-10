using System.Windows;
using FluentAssertions;
using MeowsCut.App.Views.Dialogs;
using MeowsCut.App.Views.Panes;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Diagnostics;

namespace MeowsCut.App.Tests.Views;

/// <summary>
/// Запускает код в STA-потоке с поднятым Application и словарями ресурсов.
/// </summary>
/// <remarks>
/// Без этого WPF-элементы не создаются вовсе, а ссылки на кисти и стили
/// не находятся — то есть проверялось бы не то, что работает у пользователя.
/// </remarks>
internal static class WpfRunner
{
    public static void Run(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                EnsureApplication();
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("Разметка не создалась: " + failure.Message, failure);
        }
    }

    private static void EnsureApplication()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        foreach (var source in new[] { "Theme.xaml", "Controls.xaml", "Templates.xaml" })
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/MeowsCut;component/Resources/{source}")
            });
        }
    }
}

/// <summary>
/// Разметка собирается компилятором, но ссылки на ресурсы и стили проверяются
/// только при создании элемента. Эти тесты ловят такие опечатки без запуска
/// приложения — иначе они всплывают уже у пользователя.
/// </summary>
[Collection("WPF")]
public class XamlSmokeTests
{
    [Fact]
    public void Panes_are_created() => WpfRunner.Run(() =>
    {
        new ExportPanelView().Should().NotBeNull();
        new InspectorView().Should().NotBeNull();
        new PresetsView().Should().NotBeNull();
        new TimelinePaneView().Should().NotBeNull();
        new PreviewPaneView().Should().NotBeNull();
    });

    [Fact]
    public void Error_dialog_shows_the_error_it_was_given() => WpfRunner.Run(() =>
    {
        var error = new AppError(
            ErrorCode.FfmpegFailed,
            "Не удалось обработать видео",
            "На диске не хватает места.",
            "Освободите место и попробуйте снова.",
            "Код завершения: 1");

        var dialog = new ErrorDialog(error, new AppPaths(), new NoopShell());

        dialog.DataContext.Should().Be(error);
    });

    [Fact]
    public void Settings_window_is_created() => WpfRunner.Run(() =>
    {
        // Модель здесь не нужна: проверяется, что все ресурсы разметки на месте.
        new SettingsWindow(null!).Should().NotBeNull();
    });

    private sealed class NoopShell : IShellIntegration
    {
        public void OpenFile(string path)
        {
        }

        public void RevealInExplorer(string path)
        {
        }
    }
}
