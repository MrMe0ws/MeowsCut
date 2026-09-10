using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using MeowsCut.App.Localization;
using MeowsCut.App.Timeline;
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

        foreach (var source in new[] { "Theme.xaml", "Icons.xaml", "Controls.xaml", "Templates.xaml" })
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
        new AudioInspectorView().Should().NotBeNull();
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
    public void Razor_cursor_is_built_from_the_icon() => WpfRunner.Run(() =>
    {
        // Курсор собирается вручную в формат .cur. Если байты собраны неверно,
        // Windows молча не примет их — и остаётся запасной системный крестик.
        ToolCursors.Razor.Should().NotBeSameAs(System.Windows.Input.Cursors.Cross);
    });

    [Fact]
    public void Export_window_is_created() => WpfRunner.Run(() =>
    {
        // Модели не нужны: проверяется, что все кисти и стили окна на месте.
        new ExportWindow(null!, null!).Should().NotBeNull();
    });

    [Fact]
    public void Settings_window_is_created() => WpfRunner.Run(() =>
    {
        // Модель здесь не нужна: проверяется, что все ресурсы разметки на месте.
        new SettingsWindow(null!).Should().NotBeNull();
    });

    [Fact]
    public void Finished_export_offers_the_folder_outside_the_scroll() => WpfRunner.Run(() =>
    {
        // Кнопки результата когда-то жили в прокручиваемой части и появлялись
        // под всеми настройками — до них надо было листать, и пользователь их не нашёл.
        var view = new ExportPanelView { DataContext = new FinishedExport() };

        view.Measure(new Size(340, 800));
        view.Arrange(new Rect(0, 0, 340, 800));
        view.UpdateLayout();

        var button = Find<TextBlock>(view, block => block.Text == Strings.OpenResultFolder);

        button.Should().NotBeNull("после экспорта нужен путь к готовому файлу");

        // IsVisible требует живого окна, поэтому смотрим на саму видимость:
        // ветка разметки не должна быть свёрнута ни на одном уровне.
        var branch = Ancestors(button!).Prepend(button!).OfType<UIElement>();

        branch.Should().OnlyContain(element => element.Visibility == Visibility.Visible);
        Ancestors(button!).OfType<ScrollViewer>().Should().BeEmpty("кнопку не должно уносить прокруткой");
    });

    private static T? Find<T>(DependencyObject root, Func<T, bool> match)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T typed && match(typed))
            {
                return typed;
            }

            if (Find(child, match) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject node)
    {
        while (VisualTreeHelper.GetParent(node) is { } parent)
        {
            yield return parent;
            node = parent;
        }
    }

    /// <summary>Состояние «экспорт завершён»: разметка читает эти свойства по именам.</summary>
    private sealed class FinishedExport
    {
        public bool IsDone => true;

        public bool IsIdle => false;

        public bool IsRunning => false;
    }

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
