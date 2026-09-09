using MeowsCut.Core.Abstractions;
using MeowsCut.Core.Diagnostics;

namespace MeowsCut.Ffmpeg.Toolset;

/// <summary>
/// Держит найденный набор инструментов. Сервисы обращаются сюда, а не к локатору:
/// поиск делается один раз на старте.
/// </summary>
public sealed class MediaToolsetProvider : IMediaToolsetProvider
{
    private MediaToolset? _current;

    public MediaToolset? Current => _current;

    public event EventHandler? Changed;

    public MediaToolset Require() =>
        _current ?? throw new FfmpegNotFoundException(
            "FFmpeg не найден. Укажите папку с ffmpeg.exe в настройках приложения.");

    public void Set(MediaToolset toolset)
    {
        _current = toolset;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
