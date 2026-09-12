using System.Text.Json.Serialization;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Core.Projects;

/// <summary>
/// Черновик проекта на диске: файлы и то, что из них собрано на доске.
/// </summary>
/// <remarks>
/// Характеристики файлов — длительность, кадр, кодеки — здесь намеренно не хранятся:
/// при открытии они перечитываются с самих файлов. Иначе черновик, сделанный до
/// перезаписи исходника, уверял бы, что в файле есть минуты, которых там уже нет.
/// Хранится только то, чего в файлах нет: как их порезали и разложили.
/// </remarks>
public sealed class ProjectDocument
{
    /// <summary>Версия формата. Меняется, когда старый черновик перестаёт читаться.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("sources")]
    public IReadOnlyList<ProjectSourceDocument> Sources { get; init; } = [];

    [JsonPropertyName("format")]
    public ProjectFormatDocument? Format { get; init; }

    [JsonPropertyName("clips")]
    public IReadOnlyList<ProjectClipDocument> Clips { get; init; } = [];

    [JsonPropertyName("audioTracks")]
    public IReadOnlyList<ProjectAudioTrackDocument> AudioTracks { get; init; } = [];

    [JsonPropertyName("titles")]
    public IReadOnlyList<ProjectTitleDocument> Titles { get; init; } = [];
}

/// <summary>Надпись поверх кадра.</summary>
public sealed class ProjectTitleDocument
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("startMs")]
    public double StartMs { get; init; }

    [JsonPropertyName("durationMs")]
    public double DurationMs { get; init; }

    [JsonPropertyName("scale")]
    public double Scale { get; init; } = 0.06;

    /// <summary>Место на кадре: индекс <c>TitleAnchor</c>.</summary>
    [JsonPropertyName("anchor")]
    public int Anchor { get; init; } = (int)TitleAnchor.BottomCenter;

    [JsonPropertyName("color")]
    public string Color { get; init; } = "#FFFFFF";

    [JsonPropertyName("backdrop")]
    public bool Backdrop { get; init; } = true;

    [JsonPropertyName("marginShare")]
    public double MarginShare { get; init; } = 0.05;
}

/// <summary>Файл проекта. Идентификатор хранится, чтобы клипы знали, чей они кусок.</summary>
public sealed class ProjectSourceDocument
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

public sealed class ProjectFormatDocument
{
    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("frameRateNumerator")]
    public int FrameRateNumerator { get; init; }

    [JsonPropertyName("frameRateDenominator")]
    public int FrameRateDenominator { get; init; } = 1;

    /// <summary>Формат выбран вручную, а не взят у первого файла.</summary>
    [JsonPropertyName("custom")]
    public bool Custom { get; init; }

    /// <summary>Как кусок ложится в кадр: 0 — вписать, 1 — заполнить, 2 — растянуть.</summary>
    [JsonPropertyName("fit")]
    public int Fit { get; init; }
}

public sealed class ProjectClipDocument
{
    [JsonPropertyName("sourceId")]
    public Guid SourceId { get; init; }

    /// <summary>Начало и конец куска внутри источника, в миллисекундах.</summary>
    [JsonPropertyName("sourceStartMs")]
    public double SourceStartMs { get; init; }

    [JsonPropertyName("sourceEndMs")]
    public double SourceEndMs { get; init; }

    [JsonPropertyName("leadingGapMs")]
    public double LeadingGapMs { get; init; }

    [JsonPropertyName("speed")]
    public double Speed { get; init; } = 1d;

    [JsonPropertyName("audioEnabled")]
    public bool AudioEnabled { get; init; } = true;

    [JsonPropertyName("volume")]
    public double Volume { get; init; } = 1d;

    [JsonPropertyName("zoom")]
    public double Zoom { get; init; } = 1d;

    [JsonPropertyName("offsetX")]
    public double OffsetX { get; init; }

    [JsonPropertyName("offsetY")]
    public double OffsetY { get; init; }

    /// <summary>Поворот кадра по часовой стрелке: 0, 90, 180 или 270.</summary>
    [JsonPropertyName("rotation")]
    public int Rotation { get; init; }

    [JsonPropertyName("fadeInMs")]
    public double FadeInMs { get; init; }

    [JsonPropertyName("fadeOutMs")]
    public double FadeOutMs { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }
}

public sealed class ProjectAudioTrackDocument
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("muted")]
    public bool Muted { get; init; }

    [JsonPropertyName("gain")]
    public double Gain { get; init; } = 1d;

    [JsonPropertyName("clips")]
    public IReadOnlyList<ProjectAudioClipDocument> Clips { get; init; } = [];
}

public sealed class ProjectAudioClipDocument
{
    [JsonPropertyName("sourceId")]
    public Guid SourceId { get; init; }

    [JsonPropertyName("sourceStartMs")]
    public double SourceStartMs { get; init; }

    [JsonPropertyName("sourceEndMs")]
    public double SourceEndMs { get; init; }

    [JsonPropertyName("timelineStartMs")]
    public double TimelineStartMs { get; init; }

    [JsonPropertyName("gain")]
    public double Gain { get; init; } = 1d;

    [JsonPropertyName("pitchSemitones")]
    public int PitchSemitones { get; init; }

    [JsonPropertyName("speed")]
    public double Speed { get; init; } = 1d;

    [JsonPropertyName("fadeInMs")]
    public double FadeInMs { get; init; }

    [JsonPropertyName("fadeOutMs")]
    public double FadeOutMs { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;
}
