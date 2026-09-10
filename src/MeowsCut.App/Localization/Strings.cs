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

    public static string FieldSpeedCustomHint => Get(nameof(FieldSpeedCustomHint));

    public static string SectionSources => Get(nameof(SectionSources));

    public static string SourceKindVideo => Get(nameof(SourceKindVideo));

    public static string SourceKindAudio => Get(nameof(SourceKindAudio));

    public static string SourceKindImage => Get(nameof(SourceKindImage));

    public static string SourceAddToBoard => Get(nameof(SourceAddToBoard));

    public static string SourceShowInFolder => Get(nameof(SourceShowInFolder));

    public static string FilterMediaFiles => Get(nameof(FilterMediaFiles));

    public static string FilterImageFiles => Get(nameof(FilterImageFiles));

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

    public static string ToolSelectHint => Get(nameof(ToolSelectHint));

    public static string ToolRazorHint => Get(nameof(ToolRazorHint));

    public static string Play => Get(nameof(Play));

    public static string Pause => Get(nameof(Pause));

    public static string OpenResultFolder => Get(nameof(OpenResultFolder));

    public static string SplitHereHint => Get(nameof(SplitHereHint));

    public static string DeleteClipHint => Get(nameof(DeleteClipHint));

    public static string AddVideo => Get(nameof(AddVideo));

    public static string AddVideoHint => Get(nameof(AddVideoHint));

    public static string OpenExportStep => Get(nameof(OpenExportStep));

    public static string ExportStepTitle => Get(nameof(ExportStepTitle));

    public static string SectionFrame => Get(nameof(SectionFrame));

    public static string SectionFrameHint => Get(nameof(SectionFrameHint));

    public static string TimelineSummary => Get(nameof(TimelineSummary));

    public static string AudioTrackDefaultName => Get(nameof(AudioTrackDefaultName));

    public static string AudioTrackNumbered => Get(nameof(AudioTrackNumbered));

    public static string AddSound => Get(nameof(AddSound));

    public static string AddSoundHint => Get(nameof(AddSoundHint));

    public static string AddAudioTrack => Get(nameof(AddAudioTrack));

    public static string AddAudioTrackHint => Get(nameof(AddAudioTrackHint));

    public static string DetachAudio => Get(nameof(DetachAudio));

    public static string DetachAudioHint => Get(nameof(DetachAudioHint));

    public static string SectionSound => Get(nameof(SectionSound));

    public static string FieldPitch => Get(nameof(FieldPitch));

    public static string FieldGain => Get(nameof(FieldGain));

    public static string FieldFadeIn => Get(nameof(FieldFadeIn));

    public static string FieldFadeOut => Get(nameof(FieldFadeOut));

    public static string PitchSemitones => Get(nameof(PitchSemitones));

    public static string TrackMuted => Get(nameof(TrackMuted));

    public static string RemoveTrack => Get(nameof(RemoveTrack));

    public static string AudioPreviewNote => Get(nameof(AudioPreviewNote));

    public static string FilterAudioFiles => Get(nameof(FilterAudioFiles));

    public static string OpenAudioDialogTitle => Get(nameof(OpenAudioDialogTitle));

    public static string FileHasNoSound => Get(nameof(FileHasNoSound));

    public static string FieldHardware => Get(nameof(FieldHardware));

    public static string HardwareHint => Get(nameof(HardwareHint));

    public static string OpenSubtitleDialogTitle => Get(nameof(OpenSubtitleDialogTitle));

    public static string FilterSubtitleFiles => Get(nameof(FilterSubtitleFiles));

    public static string SectionSubtitles => Get(nameof(SectionSubtitles));

    public static string ChooseSubtitles => Get(nameof(ChooseSubtitles));

    public static string ClearSubtitles => Get(nameof(ClearSubtitles));

    public static string SubtitleModeBurn => Get(nameof(SubtitleModeBurn));

    public static string SubtitleModeEmbed => Get(nameof(SubtitleModeEmbed));

    public static string SubtitleBurnHint => Get(nameof(SubtitleBurnHint));

    public static string ToolSelect => Get(nameof(ToolSelect));

    public static string ToolRazor => Get(nameof(ToolRazor));

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


    public static string FieldQuality => Get(nameof(FieldQuality));

    public static string CrfHint => Get(nameof(CrfHint));

    public static string FieldBitrateKbps => Get(nameof(FieldBitrateKbps));

    public static string FieldTargetSize => Get(nameof(FieldTargetSize));

    public static string TargetSizeHint => Get(nameof(TargetSizeHint));

    public static string FieldAudioCodec => Get(nameof(FieldAudioCodec));

    public static string FieldAudioBitrate => Get(nameof(FieldAudioBitrate));

    public static string FieldMasterVolume => Get(nameof(FieldMasterVolume));

    public static string AdvancedSettings => Get(nameof(AdvancedSettings));

    public static string FieldEncodingSpeed => Get(nameof(FieldEncodingSpeed));

    public static string TwoPass => Get(nameof(TwoPass));

    public static string FieldKeyframeInterval => Get(nameof(FieldKeyframeInterval));


    public static string CodecReplacedNotice => Get(nameof(CodecReplacedNotice));


    public static string SectionPresets => Get(nameof(SectionPresets));

    public static string ApplyPreset => Get(nameof(ApplyPreset));

    public static string DurationFitLabel => Get(nameof(DurationFitLabel));

    public static string PresetChanges => Get(nameof(PresetChanges));

    public static string PresetViolations => Get(nameof(PresetViolations));

    public static string FixViolations => Get(nameof(FixViolations));

    public static string FieldZoom => Get(nameof(FieldZoom));

    public static string FieldOffsetX => Get(nameof(FieldOffsetX));

    public static string FieldOffsetY => Get(nameof(FieldOffsetY));

    public static string ResetTransform => Get(nameof(ResetTransform));


    public static string ErrorDetails => Get(nameof(ErrorDetails));

    public static string CopyDetails => Get(nameof(CopyDetails));

    public static string OpenLogs => Get(nameof(OpenLogs));

    public static string SectionOutput => Get(nameof(SectionOutput));

    public static string SectionMaintenance => Get(nameof(SectionMaintenance));

    public static string OutputFolderTitle => Get(nameof(OutputFolderTitle));

    public static string OutputFolderHint => Get(nameof(OutputFolderHint));

    public static string FfmpegPathHint => Get(nameof(FfmpegPathHint));

    public static string NameTemplate => Get(nameof(NameTemplate));

    public static string NameTemplateHint => Get(nameof(NameTemplateHint));

    public static string VerboseLogging => Get(nameof(VerboseLogging));

    public static string SaveSettings => Get(nameof(SaveSettings));

    public static string SettingsSaved => Get(nameof(SettingsSaved));

    public static string PresetsReloaded => Get(nameof(PresetsReloaded));

    public static string CacheCleared => Get(nameof(CacheCleared));

    public static string OpenPresetsFolder => Get(nameof(OpenPresetsFolder));

    public static string ReloadPresets => Get(nameof(ReloadPresets));

    public static string ClearCache => Get(nameof(ClearCache));

    private static string Get(string key) => LocalizationManager.Get(key);
}
