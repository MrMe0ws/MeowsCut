using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;

namespace MeowsCut.Core.Presets;

/// <summary>
/// Источник пресетов. Встроенные лежат в ресурсах, пользовательские — рядом
/// с настройками и перекрывают встроенные по идентификатору.
/// </summary>
public interface IPresetProvider
{
    IReadOnlyList<PresetGroup> Groups { get; }

    IReadOnlyList<PresetDefinition> GetPresets(string groupId);

    PresetDefinition? Find(string id);

    /// <summary>Перечитать пресеты с диска, не перезапуская приложение.</summary>
    void Reload();
}

/// <summary>Применение пресета к проекту и настройкам вывода.</summary>
public interface IPresetApplier
{
    PresetApplyResult Apply(
        PresetDefinition preset,
        Project project,
        ExportSettings settings,
        DurationFitMode durationFit = DurationFitMode.Trim);
}

/// <summary>Проверка требований площадки — отдельно от применения.</summary>
public interface IPresetValidator
{
    PresetValidationResult Validate(PresetConstraints constraints, Project project, ExportSettings settings);
}
