using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Editing;
using MeowsCut.Core.Projects;
using Microsoft.Extensions.Logging;

namespace MeowsCut.App.Services;

/// <summary>
/// Сам сохраняет монтаж по ходу работы и отдаёт его обратно после падения.
/// </summary>
/// <remarks>
/// Черновик до этого сохранялся только руками, и любое падение — своё, драйвера
/// или просто отключённое электричество — забирало с собой всю работу за сеанс.
/// Пишется отдельный файл в служебной папке, а не пользовательский черновик:
/// подменять то, что человек сохранил сам, редактор не вправе.
///
/// При обычном выходе файл удаляется. Поэтому его наличие на старте и означает
/// «в прошлый раз закончили не по-хорошему» — отдельной пометки о падении не нужно.
/// </remarks>
public sealed class AutosaveService : IDisposable
{
    /// <summary>
    /// Пауза после правки. Достаточно долгая, чтобы не писать файл на каждое
    /// движение мышью, и достаточно короткая, чтобы падение забрало секунды работы.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(8);

    private readonly AppPaths _paths;
    private readonly IProjectStore _store;
    private readonly ILogger<AutosaveService> _logger;
    private readonly DispatcherTimer _timer;

    private Project? _project;
    private string? _projectPath;
    private bool _saving;

    public AutosaveService(AppPaths paths, IProjectStore store, ILogger<AutosaveService> logger)
    {
        _paths = paths;
        _store = store;
        _logger = logger;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Delay };
        _timer.Tick += async (_, _) => await FlushAsync();
    }

    /// <summary>Монтаж изменился: отсчёт до записи начинается заново.</summary>
    public void Track(Project? project, string? projectPath)
    {
        _project = project;
        _projectPath = projectPath;

        _timer.Stop();

        if (project is not null && !project.IsEmpty)
        {
            _timer.Start();
        }
    }

    /// <summary>
    /// Есть ли несохранённый монтаж с прошлого запуска.
    /// </summary>
    /// <remarks>
    /// Битый или недописанный файл состояния — не повод мешать запуску:
    /// в таком случае предлагать нечего, и мы просто молчим.
    /// </remarks>
    public AutosaveState? FindRecovery()
    {
        if (!File.Exists(_paths.AutosaveProjectFile))
        {
            return null;
        }

        try
        {
            if (!File.Exists(_paths.AutosaveStateFile))
            {
                return new AutosaveState { SavedAt = File.GetLastWriteTime(_paths.AutosaveProjectFile) };
            }

            var json = File.ReadAllText(_paths.AutosaveStateFile);
            return JsonSerializer.Deserialize<AutosaveState>(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось прочитать состояние автосохранения");
            return null;
        }
    }

    public string RecoveryProjectPath => _paths.AutosaveProjectFile;

    /// <summary>Убирает сохранённое: монтаж либо восстановлен, либо больше не нужен.</summary>
    public void Clear()
    {
        _timer.Stop();

        Delete(_paths.AutosaveProjectFile);
        Delete(_paths.AutosaveStateFile);
    }

    public void Dispose()
    {
        _timer.Stop();
        Clear();
    }

    /// <summary>
    /// Записать прямо сейчас, не дожидаясь паузы.
    /// </summary>
    /// <remarks>
    /// Нужно проверке: ждать восьми секунд ради одного факта — плохой тест.
    /// В работе запись обычно приходит от таймера.
    /// </remarks>
    public async Task FlushAsync()
    {
        _timer.Stop();

        if (_saving || _project is not { } project || project.IsEmpty)
        {
            return;
        }

        _saving = true;
        try
        {
            Directory.CreateDirectory(_paths.AutosaveDirectory);

            await _store.SaveAsync(project, _paths.AutosaveProjectFile, CancellationToken.None)
                .ConfigureAwait(true);

            var state = new AutosaveState
            {
                ProjectPath = _projectPath,
                Title = TitleOf(project),
                SavedAt = DateTimeOffset.Now
            };

            await File.WriteAllTextAsync(
                    _paths.AutosaveStateFile,
                    JsonSerializer.Serialize(state, JsonOptions),
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Не удалось — не беда: это страховка, а не работа пользователя.
            // Мешать монтажу сообщением об этом нельзя.
            _logger.LogWarning(ex, "Автосохранение не удалось");
        }
        finally
        {
            _saving = false;
        }
    }

    private static string TitleOf(Project project) =>
        project.Sources.Count > 0
            ? Path.GetFileName(project.Sources[0].FilePath)
            : string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось убрать файл автосохранения {Path}", path);
        }
    }
}
