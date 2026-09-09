using System.Windows;
using MeowsCut.App.Services;
using MeowsCut.App.ViewModels;
using MeowsCut.App.Views;
using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Configuration;
using MeowsCut.Ffmpeg.Execution;
using MeowsCut.Ffmpeg.Probing;
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

        return services;
    }

    public static IServiceCollection AddMeowsCutFfmpeg(this IServiceCollection services)
    {
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<EncoderProbe>();
        services.AddSingleton<IMediaToolsetLocator, FfmpegToolsetLocator>();
        services.AddSingleton<IMediaToolsetProvider, MediaToolsetProvider>();
        services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();

        return services;
    }

    public static IServiceCollection AddMeowsCutUi(this IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher>(_ => new UiDispatcher(Application.Current.Dispatcher));
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IDialogService, DialogService>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ShellWindow>();

        return services;
    }
}
