using System.IO;
using FluentAssertions;
using MeowsCut.App.Services;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Media;
using MeowsCut.Core.Projects;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.App.Tests.ViewModels;

/// <summary>
/// Страховка от падения: редактор сам сохраняет монтаж и отдаёт его обратно.
/// </summary>
/// <remarks>
/// Наличие файла на старте и есть признак прерванного сеанса: при обычном
/// выходе он удаляется. Поэтому здесь стерегут две вещи — что после записи
/// монтаж находится, и что после уборки не находится.
/// </remarks>
public sealed class AutosaveTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private AppPaths Paths => new(_root, _root, _root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Временные файлы — не повод падать в тесте.
        }
    }

    private AutosaveService Create() => new(
        Paths,
        new JsonProjectStore(new UnusedProbe(), NullLogger<JsonProjectStore>.Instance),
        NullLogger<AutosaveService>.Instance);

    /// <summary>Запись черновика файлы не перечитывает — читает только открытие.</summary>
    private sealed class UnusedProbe : Core.Abstractions.IMediaProbe
    {
        public Task<MediaInfo> ProbeAsync(string path, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static Project SampleProject() => Project.FromMedia(new MediaInfo(
        @"C:\видео\клип.mp4", 1000, TimeSpan.FromSeconds(30),
        new ContainerInfo("mov,mp4", "QuickTime / MOV", 1000),
        [
            new VideoStreamInfo(0, "h264", null, null, "yuv420p",
                new FrameSize(1920, 1080), new Rational(30, 1), new Rational(30, 1),
                new Rational(1, 1), 1000, 0, false, TimeSpan.FromSeconds(30))
        ],
        [],
        []));

    [Fact]
    public void Nothing_to_recover_on_a_clean_machine()
    {
        using var service = Create();

        service.FindRecovery().Should().BeNull();
    }

    [Fact]
    public async Task Saved_montage_is_found_after_a_crash()
    {
        var service = Create();
        service.Track(SampleProject(), @"C:\черновики\мой.meows");
        await service.FlushAsync();

        var state = service.FindRecovery();

        state.Should().NotBeNull();
        state!.ProjectPath.Should().Be(@"C:\черновики\мой.meows");
        state.Title.Should().Be("клип.mp4");
        File.Exists(service.RecoveryProjectPath).Should().BeTrue();

        // Обычный выход убирает страховку — иначе следующий запуск предложил бы
        // вернуть то, с чем и так закончили по-хорошему.
        service.Dispose();

        Create().FindRecovery().Should().BeNull();
    }

    [Fact]
    public async Task Empty_project_is_not_saved()
    {
        using var service = Create();

        service.Track(Project.Empty, null);
        await service.FlushAsync();

        service.FindRecovery().Should().BeNull("сохранять нечего");
    }
}
