using System.Reflection;
using System.Text.Json;
using MeowsCut.Core.Configuration;
using MeowsCut.Core.Export;
using MeowsCut.Core.Presets.Json;
using Microsoft.Extensions.Logging;

namespace MeowsCut.Core.Presets;

/// <summary>
/// Пресеты из JSON: встроенные в приложение плюс пользовательские.
/// </summary>
/// <remarks>
/// Требования площадок меняются чаще, чем выходят версии приложения, поэтому лимиты
/// живут в данных, а не в коде. Пользовательский файл перекрывает встроенный по
/// идентификатору — так можно поправить один пресет, не копируя остальные.
/// Битый файл не роняет приложение: он пропускается с записью в лог.
/// </remarks>
public sealed class JsonPresetProvider(AppPaths paths, ILogger<JsonPresetProvider> logger) : IPresetProvider
{
    private const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, PresetGroup> _groups = [];
    private readonly Dictionary<string, PresetDefinition> _presets = [];
    private bool _loaded;

    public IReadOnlyList<PresetGroup> Groups
    {
        get
        {
            EnsureLoaded();
            return _groups.Values.OrderBy(group => group.Order).ToArray();
        }
    }

    public IReadOnlyList<PresetDefinition> GetPresets(string groupId)
    {
        EnsureLoaded();

        return _presets.Values
            .Where(preset => preset.GroupId == groupId)
            .OrderBy(preset => preset.Order)
            .ToArray();
    }

    public PresetDefinition? Find(string id)
    {
        EnsureLoaded();
        return _presets.GetValueOrDefault(id);
    }

    public void Reload()
    {
        _loaded = false;
        _groups.Clear();
        _presets.Clear();

        EnsureLoaded();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        LoadEmbedded();
        LoadUserFiles();
    }

    private void LoadEmbedded()
    {
        var assembly = typeof(JsonPresetProvider).Assembly;

        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(name => name.Contains(".Presets.Definitions.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            LoadFrom(reader.ReadToEnd(), name);
        }
    }

    private void LoadUserFiles()
    {
        if (!Directory.Exists(paths.UserPresetsDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(paths.UserPresetsDirectory, "*.json"))
        {
            try
            {
                LoadFrom(File.ReadAllText(file), file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Не удалось прочитать пресеты из {File}", file);
            }
        }
    }

    private void LoadFrom(string json, string origin)
    {
        PresetFileJson? file;

        try
        {
            file = JsonSerializer.Deserialize<PresetFileJson>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Пресеты из {Origin} пропущены: файл повреждён", origin);
            return;
        }

        if (file is null)
        {
            return;
        }

        if (file.SchemaVersion != SupportedSchemaVersion)
        {
            logger.LogWarning(
                "Пресеты из {Origin} пропущены: версия схемы {Version} не поддерживается",
                origin,
                file.SchemaVersion);

            return;
        }

        var group = ReadGroup(file.Group);
        if (group is null)
        {
            return;
        }

        _groups[group.Id] = group;

        foreach (var preset in file.Presets ?? [])
        {
            var definition = ReadPreset(preset, group.Id, origin);
            if (definition is not null)
            {
                _presets[definition.Id] = definition;
            }
        }
    }

    private PresetGroup? ReadGroup(PresetGroupJson? json)
    {
        if (json?.Id is not { Length: > 0 } id || json.Title is not { Length: > 0 } title)
        {
            logger.LogWarning("Пресеты пропущены: у группы нет идентификатора или названия");
            return null;
        }

        return new PresetGroup(id, title, json.Description, json.Order);
    }

    private PresetDefinition? ReadPreset(PresetJson json, string groupId, string origin)
    {
        if (json.Id is not { Length: > 0 } id || json.Title is not { Length: > 0 } title)
        {
            logger.LogWarning("Пресет из {Origin} пропущен: нет идентификатора или названия", origin);
            return null;
        }

        return new PresetDefinition(
            id,
            groupId,
            title,
            json.Description ?? string.Empty,
            json.Order,
            ReadTemplate(json.Template),
            ReadConstraints(json.Constraints),
            json.Notes ?? []);
    }

    private static PresetTemplate ReadTemplate(PresetTemplateJson? json)
    {
        if (json is null)
        {
            return new PresetTemplate();
        }

        return new PresetTemplate
        {
            Container = ParseEnum<ContainerFormat>(json.Container),
            VideoCodec = ParseEnum<VideoCodec>(json.VideoCodec),
            CrfOverride = json.Crf,
            BitrateKbps = json.BitrateKbps,
            TargetSizeBytes = json.TargetSizeBytes,
            Width = json.Width,
            Height = json.Height,
            Fit = ParseEnum<FitMode>(json.Fit),
            Fps = json.Fps,
            PixelFormat = json.PixelFormat,
            AudioEnabled = json.AudioEnabled,
            AudioCodec = ParseEnum<AudioCodec>(json.AudioCodec),
            AudioBitrateKbps = json.AudioBitrateKbps,
            AudioSampleRateHz = json.AudioSampleRateHz,
            AudioChannels = json.AudioChannels
        };
    }

    private static PresetConstraints ReadConstraints(PresetConstraintsJson? json)
    {
        if (json is null)
        {
            return new PresetConstraints();
        }

        return new PresetConstraints
        {
            MaxDuration = json.MaxDurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            MaxFileSizeBytes = json.MaxFileSizeBytes,
            MaxWidth = json.MaxWidth,
            MaxHeight = json.MaxHeight,
            RequireSquare = json.RequireSquare,
            MaxFps = json.MaxFps,
            ForbidAudio = json.ForbidAudio,
            AllowedContainers = ParseEnumList<ContainerFormat>(json.AllowedContainers),
            AllowedVideoCodecs = ParseEnumList<VideoCodec>(json.AllowedVideoCodecs)
        };
    }

    private static T? ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static IReadOnlyList<T>? ParseEnumList<T>(List<string>? values) where T : struct, Enum
    {
        if (values is null)
        {
            return null;
        }

        var parsed = values
            .Select(ParseEnum<T>)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return parsed.Length > 0 ? parsed : null;
    }
}
