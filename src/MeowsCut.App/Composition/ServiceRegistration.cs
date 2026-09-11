using System.Windows;
using MeowsCut.App.Services;
using MeowsCut.App.Timeline;
using MeowsCut.App.Theming;
using MeowsCut.App.ViewModels;
using MeowsCut.App.Views;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Diagnostics;
using MeowsCut.Core.Jobs;
using MeowsCut.Core.Presets;
using MeowsCut.Core.Processing;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Planning;
using MeowsCut.Ffmpeg.Probing;
using MeowsCut.Ffmpeg.Rendering;
using MeowsCut.Ffmpeg.Temp;
using MeowsCut.Ffmpeg.Thumbnails;
using MeowsCut.Ffmpeg.Toolset;
using Microsoft.Extensions.DependencyInjection;

namespace MeowsCut.App.Composition;

/// <summary>
/// Единственное место, где UI знает о конкретных реализациях. Всё остальное работает
/// через интерфейсы ядра.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddMeowsCutCore(this IServiceCollection services)
    {
        services.AddSingleton<AppPaths>();
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IJobQueue, JobQueue>();
        services.AddSingleton<IPresetProvider, JsonPresetProvider>();
        services.AddSingleton<IPresetValidator, PresetValidator>();
        services.AddSingleton<IPresetApplier, PresetApplier>();
        services.AddSingleton<IErrorPresenter, ErrorPresenter>();

        return services;
    }

    public static IServiceCollection AddMeowsCutFfmpeg(this IServiceCollection services)
    {
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<EncoderProbe>();
        services.AddSingleton<IMediaToolsetLocator, FfmpegToolsetLocator>();
        services.AddSingleton<IMediaToolsetProvider, MediaToolsetProvider>();
        services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
        services.AddSingleton<IThumbnailService, FfmpegThumbnailService>();
        services.AddSingleton<IWaveformService, FfmpegWaveformService>();
        services.AddSingleton<IExportPlanner, FfmpegExportPlanner>();
        services.AddSingleton<ITempWorkspaceFactory, TempWorkspaceFactory>();
        services.AddTransient<IExportEngine, FfmpegExportEngine>();

        return services;
    }

    public static IServiceCollection AddMeowsCutUi(this IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher>(_ => new UiDispatcher(Application.Current.Dispatcher));
        services.AddSingleton(_ => new ThemeManager(Application.Current.Resources));
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<Func<SettingsViewModel>>(provider => provider.GetRequiredService<SettingsViewModel>);
        services.AddSingleton<IShellIntegration, ShellIntegration>();

        services.AddSingleton<ThumbnailImageCache>();
        services.AddSingleton<TimelineThumbnailLoader>();
        services.AddSingleton<AudioWaveformCache>();

        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<PreviewViewModel>();
        services.AddSingleton<InspectorViewModel>();
        services.AddSingleton<AudioInspectorViewModel>();
        services.AddSingleton<ExportViewModel>();
        services.AddSingleton<PresetsViewModel>();
        services.AddSingleton<Func<PresetsViewModel>>(provider => provider.GetRequiredService<PresetsViewModel>);
        services.AddSingleton<Func<ExportViewModel>>(provider => provider.GetRequiredService<ExportViewModel>);
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ShellWindow>();

        return services;
    }
}
