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

    public static string SectionExport => Get(nameof(SectionExport));

    public static string SectionSummary => Get(nameof(SectionSummary));

    public static string FieldContainerFormat => Get(nameof(FieldContainerFormat));

    public static string FieldVideoCodec => Get(nameof(FieldVideoCodec));

    public static string FieldOutputFile => Get(nameof(FieldOutputFile));

    public static string KeepAudio => Get(nameof(KeepAudio));

    public static string FastMode => Get(nameof(FastMode));

    public static string EstimatedSize => Get(nameof(EstimatedSize));

    public static string StartExport => Get(nameof(StartExport));

    public static string CancelExport => Get(nameof(CancelExport));

    public static string Elapsed => Get(nameof(Elapsed));

    public static string Remaining => Get(nameof(Remaining));

    public static string ExportDone => Get(nameof(ExportDone));

    public static string ExportFailed => Get(nameof(ExportFailed));

    public static string OpenResultFile => Get(nameof(OpenResultFile));

    public static string ShowInFolder => Get(nameof(ShowInFolder));

    public static string ExportAgain => Get(nameof(ExportAgain));

    public static string ToolSelect => Get(nameof(ToolSelect));

    public static string ToolRazor => Get(nameof(ToolRazor));

    public static string ToolHand => Get(nameof(ToolHand));

    public static string SplitHere => Get(nameof(SplitHere));

    public static string DeleteClip => Get(nameof(DeleteClip));

    public static string Snapping => Get(nameof(Snapping));

    public static string ZoomFit => Get(nameof(ZoomFit));

    public static string SectionClip => Get(nameof(SectionClip));

    public static string NoClipSelected => Get(nameof(NoClipSelected));

    public static string FieldOnTimeline => Get(nameof(FieldOnTimeline));

    public static string FieldInSource => Get(nameof(FieldInSource));

    public static string FieldSpeed => Get(nameof(FieldSpeed));

    public static string ClipAudioEnabled => Get(nameof(ClipAudioEnabled));

    public static string FieldVolume => Get(nameof(FieldVolume));

    public static string PreviewFragment => Get(nameof(PreviewFragment));

    public static string PreviewFragmentRunning => Get(nameof(PreviewFragmentRunning));

    public static string PreviewFragmentEmpty => Get(nameof(PreviewFragmentEmpty));

    public static string PlayerUnavailable => Get(nameof(PlayerUnavailable));

    private static string Get(string key) => LocalizationManager.Get(key);
}
