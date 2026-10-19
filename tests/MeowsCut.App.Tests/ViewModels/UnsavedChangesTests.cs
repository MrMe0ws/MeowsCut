using System.IO;
using System.Windows.Threading;
using FluentAssertions;
using MeowsCut.App.Composition;
using MeowsCut.App.Services;
using MeowsCut.App.Tests.Support;
using MeowsCut.App.Tests.Views;
using MeowsCut.App.ViewModels;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Media;
using Microsoft.Extensions.DependencyInjection;

namespace MeowsCut.App.Tests.ViewModels;

/// <summary>
/// Несохранённый монтаж не пропадает молча: ни при закрытии проекта,
/// ни при открытии другого файла.
/// </summary>
/// <remarks>
/// Пути — во временной папке: настоящие автосохранение и настройки пользователя
/// тест трогать не должен.
/// </remarks>
[Collection("WPF")]
public sealed class UnsavedChangesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meows-unsaved-" + Guid.NewGuid().ToString("N"));

    private readonly AskingDialogs _dialogs = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Fresh_file_has_nothing_to_lose() => Run(async shell =>
    {
        await shell.OpenAsync(@"C:\видео\клип.mp4", CancellationToken.None);

        shell.HasUnsavedChanges.Should().BeFalse();
        (await shell.ConfirmDiscardAsync()).Should().BeTrue();
        _dialogs.Questions.Should().Be(0, "спрашивать не о чем");
    });

    [Fact]
    public void An_edit_marks_the_project_unsaved() => Run(async shell =>
    {
        await OpenAndEditAsync(shell);

        shell.HasUnsavedChanges.Should().BeTrue();
        shell.SaveStateText.Should().Contain("не сохранено");
    });

    [Fact]
    public void Closing_with_edits_asks_and_can_be_cancelled() => Run(async shell =>
    {
        await OpenAndEditAsync(shell);
        _dialogs.Answer = null;

        await shell.CloseMediaCommand.ExecuteAsync(null);

        _dialogs.Questions.Should().Be(1);
        shell.Project.Should().NotBeNull("передумали — монтаж остаётся на месте");
    });

    [Fact]
    public void Do_not_save_closes_the_project() => Run(async shell =>
    {
        await OpenAndEditAsync(shell);
        _dialogs.Answer = false;

        await shell.CloseMediaCommand.ExecuteAsync(null);

        shell.Project.Should().BeNull();
    });

    [Fact]
    public void Opening_another_file_over_edits_asks_first() => Run(async shell =>
    {
        await OpenAndEditAsync(shell);
        var edited = shell.Project;
        _dialogs.Answer = null;

        await shell.OpenAsync(@"C:\видео\другой.mp4", CancellationToken.None);

        _dialogs.Questions.Should().Be(1);
        shell.Project.Should().BeSameAs(edited, "иначе правки пропали бы, как с восстановленным монтажом");
    });

    private static async Task OpenAndEditAsync(ShellViewModel shell)
    {
        await shell.OpenAsync(@"C:\видео\клип.mp4", CancellationToken.None);
        shell.Timeline.Playhead = TimeSpan.FromSeconds(5);
        shell.Timeline.SplitAtPlayheadCommand.Execute(null);
    }

    private void Run(Func<ShellViewModel, Task> body) => WpfRunner.Run(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());

        var provider = new ServiceCollection()
            .AddLogging()
            .AddMeowsCutCore()
            .AddMeowsCutFfmpeg()
            .AddMeowsCutRecording()
            .AddMeowsCutUi()
            .AddSingleton(new AppPaths(_root, _root, _root))
            .AddSingleton<IDialogService>(_dialogs)
            .AddSingleton<IMediaProbe>(new FakeProbe())
            .BuildServiceProvider();

        var shell = provider.GetRequiredService<ShellViewModel>();
        shell.IsToolsetReady = true;

        var task = body(shell);
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    });

    private sealed class FakeProbe : IMediaProbe
    {
        public Task<MediaInfo> ProbeAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(Fake.Info(20, path: filePath));
    }

    /// <summary>Отвечает на вопрос о сохранении заранее заданным ответом и считает вопросы.</summary>
    private sealed class AskingDialogs : IDialogService
    {
        public bool? Answer { get; set; }

        public int Questions { get; private set; }

        public bool? Ask(string title, string message, string acceptText, string declineText, string cancelText)
        {
            Questions++;
            return Answer;
        }

        public void ShowError(string title, string message) => throw new InvalidOperationException(message);

        public void ShowError(Core.Diagnostics.AppError error) => throw new InvalidOperationException(error.Message);

        public void ShowSettings()
        {
        }

        public void ShowExport()
        {
        }

        public bool Confirm(string title, string message, string acceptText, string declineText) => false;

        public SilenceOptions? AskSilenceOptions(SilenceOptions current) => null;
    }
}
