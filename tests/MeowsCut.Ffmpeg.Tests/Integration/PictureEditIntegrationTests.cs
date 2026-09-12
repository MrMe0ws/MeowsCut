using FluentAssertions;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Editing.Timeline;
using MeowsCut.Core.Export;
using MeowsCut.Core.Jobs;
using MeowsCut.Core.Media;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Planning;
using MeowsCut.Ffmpeg.Probing;
using MeowsCut.Ffmpeg.Rendering;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeowsCut.Ffmpeg.Tests.Integration;

/// <summary>
/// Затухание, поворот, звук без картинки и выравнивание громкости на настоящем ffmpeg.
/// </summary>
/// <remarks>
/// Строкой такое не проверить. Граф с fade и transpose выглядит правильным,
/// но повёрнутый кусок меняет размер кадра, и concat отказывается его принимать,
/// а звуковой контейнер спотыкается о висящий выход видеоцепочки. Оба отказа
/// случаются только при настоящем запуске.
/// </remarks>
[Trait("Category", "RequiresFfmpeg")]
public sealed class PictureEditIntegrationTests : IDisposable
{
    private readonly string _workDirectory = Path.Combine(
        Path.GetTempPath(),
        "meowscut-tests",
        Guid.NewGuid().ToString("N"));

    private readonly ProcessRunner _runner = new(NullLogger<ProcessRunner>.Instance);

    public PictureEditIntegrationTests() => Directory.CreateDirectory(_workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Временные файлы — не повод падать в тесте.
        }
    }

    [FfmpegFact]
    public async Task Fades_do_not_change_the_length()
    {
        var project = await CreateProjectAsync();

        var faded = WithFirstClip(project, clip =>
            clip.WithFades(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

        var output = Path.Combine(_workDirectory, "с затуханием.mp4");
        var result = await RunAsync(faded, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        // Затухание — это яркость кадра, а не обрезка: ролик обязан остаться прежней длины.
        (await ProbeAsync(output)).Duration
            .Should().BeCloseTo(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(600));
    }

    [FfmpegFact]
    public async Task Picture_appears_out_of_black_and_goes_back_into_it()
    {
        var project = await CreateProjectAsync();

        var faded = WithFirstClip(project, clip =>
            clip.WithFades(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2)));

        var output = Path.Combine(_workDirectory, "появление.mp4");
        (await RunAsync(faded, Settings(output))).Status.Should().Be(JobStatus.Completed);

        // testsrc2 — яркая пёстрая картинка, и без затухания все три замера были бы
        // одинаково светлыми. Проверяем именно форму: тёмные края, светлая середина.
        var atStart = await BrightnessAsync(output, TimeSpan.FromMilliseconds(40));
        var atMiddle = await BrightnessAsync(output, TimeSpan.FromSeconds(3));
        var atEnd = await BrightnessAsync(output, TimeSpan.FromMilliseconds(5900));

        atStart.Should().BeLessThan(20, "ролик начинается с чёрного");
        atMiddle.Should().BeGreaterThan(atStart + 20, "к середине картинка уже видна");
        atEnd.Should().BeLessThan(atMiddle, "к концу кадр снова гаснет");
    }

    [FfmpegFact]
    public async Task Rotated_clip_is_concatenated_with_an_untouched_one()
    {
        // Ровно тот случай, ради которого поворот включает приведение к общему
        // размеру: у повёрнутого куска ширина и высота меняются местами.
        var project = await CreateProjectAsync();
        var split = project.WithSequence(project.Sequence.SplitAt(TimeSpan.FromSeconds(3)));

        var rotated = WithFirstClip(split, clip =>
            clip.WithTransform(ClipTransform.Identity.WithRotation(90)));

        var output = Path.Combine(_workDirectory, "поворот.mp4");
        var result = await RunAsync(rotated, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        var info = await ProbeAsync(output);
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(600));
        info.PrimaryVideo!.Size.Should().Be(new FrameSize(320, 240), "кадр ролика остался прежним");
    }

    [FfmpegFact]
    public async Task Half_turn_survives_the_export()
    {
        var project = await CreateProjectAsync();
        var flipped = WithFirstClip(project, clip =>
            clip.WithTransform(ClipTransform.Identity.WithRotation(180)));

        var output = Path.Combine(_workDirectory, "переворот.mp4");
        var result = await RunAsync(flipped, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);
        (await ProbeAsync(output)).PrimaryVideo!.Size.Should().Be(new FrameSize(320, 240));
    }

    [FfmpegFact]
    public async Task Audio_only_export_produces_a_sound_file_without_picture()
    {
        var project = await CreateProjectAsync();

        var settings = ExportSettings.Default with
        {
            Container = ContainerFormat.Mp3,
            OutputPath = Path.Combine(_workDirectory, "дорожка.mp3")
        };

        var result = await RunAsync(project, settings);

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        var info = await ProbeAsync(settings.OutputPath);
        info.HasAudio.Should().BeTrue();
        info.PrimaryVideo.Should().BeNull("в звуковом файле картинки быть не должно");
        info.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(600));
    }

    [FfmpegFact]
    public async Task Loudness_normalization_runs_through()
    {
        var project = await CreateProjectAsync();

        var settings = Settings(Path.Combine(_workDirectory, "громкость.mp4")) with
        {
            Audio = AudioSettings.Default with { NormalizeLoudness = true }
        };

        var result = await RunAsync(project, settings);

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);
        (await ProbeAsync(settings.OutputPath)).HasAudio.Should().BeTrue();
    }

    [FfmpegFact]
    public async Task Exporting_a_slice_gives_a_file_of_that_length()
    {
        var project = await CreateProjectAsync();

        // Так же режет и переключатель «Выделенный кусок» в окне экспорта.
        var sliced = project.WithSequence(
            project.Sequence.Slice(new TimeRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4))));

        var output = Path.Combine(_workDirectory, "кусок.mp4");
        var result = await RunAsync(sliced, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        (await ProbeAsync(output)).Duration
            .Should().BeCloseTo(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(400));
    }

    [FfmpegFact]
    public async Task Horizontal_video_fits_into_a_vertical_frame()
    {
        var project = await CreateProjectAsync();
        var format = project.Sequence.Format.WithAspect(9d / 16d);
        var vertical = project.WithSequence(project.Sequence with { Format = format });

        var output = Path.Combine(_workDirectory, "вертикальный.mp4");
        var result = await RunAsync(vertical, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        // Исходник 320×240: длинная сторона 320 остаётся высотой кадра.
        (await ProbeAsync(output)).PrimaryVideo!.Size.Should().Be(new FrameSize(180, 320));
    }

    [FfmpegFact]
    public async Task Vertical_frame_can_be_filled_instead_of_padded()
    {
        var project = await CreateProjectAsync();

        var format = project.Sequence.Format.WithAspect(9d / 16d) with
        {
            Fit = Core.Export.FitMode.Cover
        };

        var vertical = project.WithSequence(project.Sequence with { Format = format });

        var output = Path.Combine(_workDirectory, "вертикальный заполненный.mp4");
        var result = await RunAsync(vertical, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);

        var info = await ProbeAsync(output);
        info.PrimaryVideo!.Size.Should().Be(new FrameSize(180, 320));

        // Заполненный кадр без полей: середина верхней строки уже картинка,
        // а не чёрная полоса, как при вписывании.
        (await BrightnessAsync(output, TimeSpan.FromSeconds(3))).Should().BeGreaterThan(20);
    }

    [FfmpegFact]
    public async Task Title_with_tricky_characters_is_drawn_over_the_frame()
    {
        // Апостроф, двоеточие и процент — ровно те символы, на которых
        // неэкранированная строка фильтра рвётся целиком.
        var project = await CreateProjectAsync();

        var title = TitleClip.Create("Итого: 100% дом'ой", TimeSpan.FromSeconds(1)) with
        {
            Duration = TimeSpan.FromSeconds(3),
            Anchor = TitleAnchor.MiddleCenter,
            Scale = 0.15
        };

        var withTitle = project.WithSequence(project.Sequence.WithTitles([title]));

        var output = Path.Combine(_workDirectory, "надпись.mp4");
        var result = await RunAsync(withTitle, Settings(output));

        result.Status.Should().Be(JobStatus.Completed, result.Error?.Message);
        (await ProbeAsync(output)).Duration
            .Should().BeCloseTo(TimeSpan.FromSeconds(6), TimeSpan.FromMilliseconds(600));
    }

    [FfmpegFact]
    public async Task Title_appears_only_inside_its_own_stretch()
    {
        // Сравниваем один и тот же кадр в двух файлах — с надписью и без.
        // Мерить яркость бесполезно: тёмная плашка и белые буквы взаимно
        // гасят друг друга, и среднее по кадру почти не меняется. А вот сам
        // кадр меняется сильно — на это и смотрим.
        var project = await CreateProjectAsync();

        var title = TitleClip.Create(new string('X', 10), TimeSpan.FromSeconds(3)) with
        {
            Duration = TimeSpan.FromSeconds(2),
            Anchor = TitleAnchor.MiddleCenter,
            Scale = 0.25
        };

        var withTitle = project.WithSequence(project.Sequence.WithTitles([title]));

        var plain = Path.Combine(_workDirectory, "без надписи.mp4");
        var titled = Path.Combine(_workDirectory, "с надписью.mp4");

        (await RunAsync(project, Settings(plain))).Status.Should().Be(JobStatus.Completed);
        (await RunAsync(withTitle, Settings(titled))).Status.Should().Be(JobStatus.Completed);

        var inside = TimeSpan.FromSeconds(4);
        var outside = TimeSpan.FromSeconds(1);

        var withTitleShown = Difference(await FrameAsync(plain, inside), await FrameAsync(titled, inside));
        var withoutTitle = Difference(await FrameAsync(plain, outside), await FrameAsync(titled, outside));

        withoutTitle.Should().BeLessThan(2, "до своего времени надписи в кадре нет");
        withTitleShown.Should().BeGreaterThan(withoutTitle * 4,
            "в свой отрезок надпись меняет кадр несопоставимо сильнее");
        withTitleShown.Should().BeGreaterThan(4, "надпись видна, а не теряется в шуме кодирования");
    }

    /// <summary>Среднее расхождение двух кадров: 0 — одинаковые, 255 — противоположные.</summary>
    private static double Difference(byte[] left, byte[] right)
    {
        left.Length.Should().Be(right.Length);

        var total = 0d;
        for (var i = 0; i < left.Length; i++)
        {
            total += Math.Abs(left[i] - right[i]);
        }

        return total / left.Length;
    }

    /// <summary>
    /// Кадр в оттенках серого, ужатый до сетки 16×16.
    /// </summary>
    /// <remarks>
    /// Сырые байты без разбора картиночных форматов и без лишних зависимостей
    /// у тестов. Шестнадцать на шестнадцать достаточно, чтобы заметить надпись
    /// посреди кадра, и достаточно грубо, чтобы не ловить шум кодирования.
    /// </remarks>
    private async Task<byte[]> FrameAsync(string path, TimeSpan at)
    {
        var raw = Path.Combine(_workDirectory, "кадр-" + Guid.NewGuid().ToString("N") + ".raw");

        string[] arguments =
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-ss", Seconds(at),
            "-i", path,
            "-frames:v", "1",
            "-vf", "scale=16:16,format=gray",
            "-f", "rawvideo", raw
        ];

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, arguments),
            null,
            null,
            CancellationToken.None);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.StdErrTail));

        var bytes = await File.ReadAllBytesAsync(raw);
        bytes.Should().HaveCount(256, "кадр в этот момент обязан существовать");

        return bytes;
    }

    private static Project WithFirstClip(Project project, Func<Clip, Clip> change) =>
        project.WithSequence(
            project.Sequence.WithTrack(
                project.Sequence.Video.Replace(change(project.Sequence.Video.Clips[0]))));

    private static ExportSettings Settings(string outputPath) =>
        ExportSettings.Default with { OutputPath = outputPath, PreferStreamCopy = false };

    /// <summary>
    /// Яркость кадра в указанный момент: 0 — чёрный, 255 — белый.
    /// </summary>
    /// <remarks>
    /// Кадр сжимается до одного пикселя в градациях серого и пишется сырыми
    /// байтами — получается ровно один байт средней яркости, без разбора
    /// картиночных форматов и без лишних зависимостей у тестов.
    /// </remarks>
    private async Task<int> BrightnessAsync(string path, TimeSpan at)
    {
        var raw = Path.Combine(_workDirectory, "яркость-" + Guid.NewGuid().ToString("N") + ".raw");

        string[] arguments =
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-ss", Seconds(at),
            "-i", path,
            "-frames:v", "1",
            "-vf", "scale=1:1,format=gray",
            "-f", "rawvideo", raw
        ];

        var result = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, arguments),
            null,
            null,
            CancellationToken.None);

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.StdErrTail));

        var bytes = await File.ReadAllBytesAsync(raw);
        bytes.Should().NotBeEmpty("кадр в этот момент обязан существовать");

        return bytes[0];
    }

    private static string Seconds(TimeSpan value) =>
        value.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Шестисекундное видео со звуком.</summary>
    private async Task<Project> CreateProjectAsync()
    {
        var path = Path.Combine(_workDirectory, "видео.mp4");

        string[] arguments =
        [
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=320x240:rate=25:duration=6",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=6",
            "-c:v", "libx264", "-preset", "ultrafast", "-crf", "32", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "64k", "-shortest", path
        ];

        var made = await _runner.RunAsync(
            new ProcessRequest(FfmpegTestEnvironment.FfmpegPath!, arguments),
            null,
            null,
            CancellationToken.None);

        made.Success.Should().BeTrue(string.Join(Environment.NewLine, made.StdErrTail));

        return Project.FromMedia(await ProbeAsync(path));
    }

    private async Task<JobResult> RunAsync(Project project, ExportSettings settings)
    {
        var plan = new FfmpegExportPlanner(new AppPaths()).CreatePlan(
            new ExportRequest(project, settings),
            new MediaCapabilities(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "libx264" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "libmp3lame" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            {
                // drawtext заявляем отдельно: без него планировщик надписи в граф
                // не кладёт, и проверка рисования молча проверяла бы пустоту.
                Filters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "drawtext" }
            });

        var engine = new FfmpegExportEngine(
            CreateToolsetProvider(),
            _runner,
            NullLogger<FfmpegExportEngine>.Instance);

        return await engine.ExecuteAsync(plan, new Progress<JobProgress>(), CancellationToken.None);
    }

    private async Task<MediaInfo> ProbeAsync(string path) =>
        await new FfprobeMediaProbe(CreateToolsetProvider(), _runner, NullLogger<FfprobeMediaProbe>.Instance)
            .ProbeAsync(path, CancellationToken.None);

    private static IMediaToolsetProvider CreateToolsetProvider()
    {
        var provider = new MediaToolsetProvider();
        provider.Set(new MediaToolset(
            FfmpegTestEnvironment.FfmpegPath!,
            FfmpegTestEnvironment.FfprobePath!,
            "test",
            ToolsetSource.LocalAppData,
            MediaCapabilities.Empty));

        return provider;
    }
}
