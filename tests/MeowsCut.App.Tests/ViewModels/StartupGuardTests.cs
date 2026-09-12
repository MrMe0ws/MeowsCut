using FluentAssertions;
using MeowsCut.App.Composition;
using MeowsCut.App.Tests.Views;
using MeowsCut.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.Tests.ViewModels;

/// <summary>
/// Пока идёт стартовая подготовка, открывать нечем: ffmpeg ещё не найден.
/// Раньше пункты меню в это время работали и упирались в сообщение
/// «FFmpeg не найден» — про программу, которая просто не успела запуститься.
/// </summary>
[Collection("WPF")]
public class StartupGuardTests
{
    [Fact]
    public void Files_cannot_be_opened_until_the_toolset_is_found() => WpfRunner.Run(() =>
    {
        var shell = BuildShell();

        shell.CanStartWork.Should().BeFalse("ffmpeg ещё ищется");
        shell.OpenFileCommand.CanExecute(null).Should().BeFalse();
        shell.OpenProjectCommand.CanExecute(null).Should().BeFalse();
        shell.AddFileCommand.CanExecute(null).Should().BeFalse();
        shell.AddSoundCommand.CanExecute(null).Should().BeFalse();
    });

    [Fact]
    public void Menu_wakes_up_when_the_toolset_is_ready() => WpfRunner.Run(() =>
    {
        var shell = BuildShell();

        shell.IsToolsetReady = true;

        shell.CanStartWork.Should().BeTrue();
        shell.OpenFileCommand.CanExecute(null).Should().BeTrue();
        shell.OpenProjectCommand.CanExecute(null).Should().BeTrue();
    });

    [Fact]
    public void Reading_a_file_closes_the_menu_for_the_next_one() => WpfRunner.Run(() =>
    {
        // Второй файл поверх читающегося первого молча терялся: команда отрабатывала
        // и тут же выходила по IsBusy. Теперь пункт просто недоступен.
        var shell = BuildShell();

        shell.IsToolsetReady = true;
        shell.IsBusy = true;

        shell.CanStartWork.Should().BeFalse();
        shell.OpenFileCommand.CanExecute(null).Should().BeFalse();
    });

    private static ShellViewModel BuildShell()
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddMeowsCutCore()
            .AddMeowsCutFfmpeg()
            .AddMeowsCutUi()
            .BuildServiceProvider();

        return provider.GetRequiredService<ShellViewModel>();
    }
}
