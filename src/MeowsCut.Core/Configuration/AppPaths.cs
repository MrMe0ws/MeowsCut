namespace MeowsCut.Core.Configuration;

/// <summary>
/// Единственное место, где вычисляются пути приложения. Никаких
/// Environment.GetFolderPath по коду — иначе они разъедутся.
/// </summary>
public sealed class AppPaths
{
    private const string AppFolderName = "MeowsCut";

    public AppPaths()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory)
    {
    }

    public AppPaths(string localAppData, string roamingAppData, string applicationDirectory)
    {
        ApplicationDirectory = applicationDirectory;
        LocalRoot = Path.Combine(localAppData, AppFolderName);
        RoamingRoot = Path.Combine(roamingAppData, AppFolderName);
    }

    /// <summary>Папка, откуда запущено приложение (там же лежит ffmpeg в portable-поставке).</summary>
    public string ApplicationDirectory { get; }

    public string LocalRoot { get; }

    public string RoamingRoot { get; }

    public string LogsDirectory => Path.Combine(LocalRoot, "logs");

    public string TempDirectory => Path.Combine(LocalRoot, "temp");

    /// <summary>Короткие фрагменты для проверки настроек перед полным экспортом.</summary>
    public string PreviewDirectory => Path.Combine(LocalRoot, "temp", "preview");

    public string ThumbnailsDirectory => Path.Combine(LocalRoot, "thumbs");

    /// <summary>Картинки звуковых волн. Отдельно от кадров: чистятся они по-разному.</summary>
    public string WaveformsDirectory => Path.Combine(LocalRoot, "waveforms");

    /// <summary>Папка ffmpeg для разработки: бинарники не хранятся в репозитории.</summary>
    public string LocalFfmpegDirectory => Path.Combine(LocalRoot, "ffmpeg");

    /// <summary>Папка ffmpeg в portable-поставке — рядом с исполняемым файлом.</summary>
    public string BundledFfmpegDirectory => Path.Combine(ApplicationDirectory, "ffmpeg");

    /// <summary>
    /// Черновик, который редактор сохраняет сам по ходу работы.
    /// </summary>
    /// <remarks>
    /// Рядом с временными файлами, а не в папке пользователя: это не его файл,
    /// а страховка редактора, и место ей там же, где кэшу кадров.
    /// </remarks>
    public string AutosaveDirectory => Path.Combine(LocalRoot, "autosave");

    public string AutosaveProjectFile => Path.Combine(AutosaveDirectory, "recovery.meows");

    public string AutosaveStateFile => Path.Combine(AutosaveDirectory, "recovery.json");

    public string SettingsFile => Path.Combine(RoamingRoot, "settings.json");

    public string UserPresetsDirectory => Path.Combine(RoamingRoot, "presets");

    /// <summary>
    /// Создаёт папки, которые нужны приложению для работы. Вызывается один раз на старте.
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
        Directory.CreateDirectory(WaveformsDirectory);
        Directory.CreateDirectory(AutosaveDirectory);
        Directory.CreateDirectory(RoamingRoot);
    }
}
