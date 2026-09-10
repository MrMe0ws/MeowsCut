using System.Text.Json.Serialization;

namespace MeowsCut.Core.Presets.Json;

/// <summary>
/// Файл пресетов. Версия схемы обязательна: требования площадок меняются,
/// и загрузчик должен уметь отличить свой формат от чужого.
/// </summary>
internal sealed class PresetFileJson
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("group")]
    public PresetGroupJson? Group { get; set; }

    [JsonPropertyName("presets")]
    public List<PresetJson>? Presets { get; set; }
}

internal sealed class PresetGroupJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

internal sealed class PresetJson
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("template")]
    public PresetTemplateJson? Template { get; set; }

    [JsonPropertyName("constraints")]
    public PresetConstraintsJson? Constraints { get; set; }

    [JsonPropertyName("notes")]
    public List<string>? Notes { get; set; }
}

internal sealed class PresetTemplateJson
{
    [JsonPropertyName("container")]
    public string? Container { get; set; }

    [JsonPropertyName("videoCodec")]
    public string? VideoCodec { get; set; }

    [JsonPropertyName("crf")]
    public int? Crf { get; set; }

    [JsonPropertyName("bitrateKbps")]
    public int? BitrateKbps { get; set; }

    [JsonPropertyName("targetSizeBytes")]
    public long? TargetSizeBytes { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("fit")]
    public string? Fit { get; set; }

    [JsonPropertyName("fps")]
    public double? Fps { get; set; }

    [JsonPropertyName("pixelFormat")]
    public string? PixelFormat { get; set; }

    [JsonPropertyName("audioEnabled")]
    public bool? AudioEnabled { get; set; }

    [JsonPropertyName("audioCodec")]
    public string? AudioCodec { get; set; }

    [JsonPropertyName("audioBitrateKbps")]
    public int? AudioBitrateKbps { get; set; }

    [JsonPropertyName("audioSampleRateHz")]
    public int? AudioSampleRateHz { get; set; }

    [JsonPropertyName("audioChannels")]
    public int? AudioChannels { get; set; }
}

internal sealed class PresetConstraintsJson
{
    [JsonPropertyName("maxDurationSeconds")]
    public double? MaxDurationSeconds { get; set; }

    [JsonPropertyName("maxFileSizeBytes")]
    public long? MaxFileSizeBytes { get; set; }

    [JsonPropertyName("maxWidth")]
    public int? MaxWidth { get; set; }

    [JsonPropertyName("maxHeight")]
    public int? MaxHeight { get; set; }

    [JsonPropertyName("requireSquare")]
    public bool RequireSquare { get; set; }

    [JsonPropertyName("maxFps")]
    public double? MaxFps { get; set; }

    [JsonPropertyName("forbidAudio")]
    public bool ForbidAudio { get; set; }

    [JsonPropertyName("allowedContainers")]
    public List<string>? AllowedContainers { get; set; }

    [JsonPropertyName("allowedVideoCodecs")]
    public List<string>? AllowedVideoCodecs { get; set; }
}
