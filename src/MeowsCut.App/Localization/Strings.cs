namespace MeowsCut.App.Localization;

/// <summary>
/// Типизированный доступ к строкам из Strings.resx. Написан руками, а не генератором:
/// так строки одинаково доступны из C# и из XAML (через loc:Tr), а добавление языка
/// сводится к новому resx-файлу.
/// </summary>
public static class Strings
{
    public static string AppTitle => Get(nameof(AppTitle));

    public static string EmptyStateTitle => Get(nameof(EmptyStateTitle));

    public static string EmptyStateOr => Get(nameof(EmptyStateOr));

    public static string OpenVideo => Get(nameof(OpenVideo));

    public static string OpenDialogTitle => Get(nameof(OpenDialogTitle));

    public static string FilterVideoFiles => Get(nameof(FilterVideoFiles));

    public static string FilterAllFiles => Get(nameof(FilterAllFiles));

    public static string RecentFiles => Get(nameof(RecentFiles));

    public static string Close => Get(nameof(Close));

    public static string Settings => Get(nameof(Settings));

    public static string Reading => Get(nameof(Reading));

    public static string SectionFile => Get(nameof(SectionFile));

    public static string SectionVideo => Get(nameof(SectionVideo));

    public static string SectionAudio => Get(nameof(SectionAudio));

    public static string FieldDuration => Get(nameof(FieldDuration));

    public static string FieldSize => Get(nameof(FieldSize));

    public static string FieldContainer => Get(nameof(FieldContainer));

    public static string FieldOverallBitrate => Get(nameof(FieldOverallBitrate));

    public static string FieldResolution => Get(nameof(FieldResolution));

    public static string FieldFrameRate => Get(nameof(FieldFrameRate));

    public static string FieldCodec => Get(nameof(FieldCodec));

    public static string FieldBitrate => Get(nameof(FieldBitrate));

    public static string FieldPixelFormat => Get(nameof(FieldPixelFormat));

    public static string FieldChannels => Get(nameof(FieldChannels));

    public static string FieldSampleRate => Get(nameof(FieldSampleRate));

    public static string NoAudio => Get(nameof(NoAudio));

    public static string Unknown => Get(nameof(Unknown));

    public static string WarningVariableFrameRate => Get(nameof(WarningVariableFrameRate));

    public static string WarningRotated => Get(nameof(WarningRotated));

    public static string FfmpegSearching => Get(nameof(FfmpegSearching));

    public static string FfmpegReady => Get(nameof(FfmpegReady));

    public static string FfmpegNotFoundTitle => Get(nameof(FfmpegNotFoundTitle));

    public static string FfmpegNotFoundMessage => Get(nameof(FfmpegNotFoundMessage));

    public static string FfmpegChooseFolder => Get(nameof(FfmpegChooseFolder));

    public static string FfmpegChooseFolderTitle => Get(nameof(FfmpegChooseFolderTitle));

    public static string ErrorTitle => Get(nameof(ErrorTitle));

    public static string CloseFile => Get(nameof(CloseFile));

    private static string Get(string key) => LocalizationManager.Get(key);
}
