using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;

namespace MeowsCut.Core.Presets;

/// <summary>Одно изменение, которое внёс пресет. Показывается пользователю списком.</summary>
public sealed record PresetChange(string Description);

public enum ViolationSeverity
{
    /// <summary>Экспорт с таким нарушением бессмысленен: площадка файл не примет.</summary>
    Error = 0,

    /// <summary>Скорее всего примет, но результат будет не тем, чего ждут.</summary>
    Warning
}

/// <summary>Какое действие способно устранить нарушение автоматически.</summary>
public enum AutoFixKind
{
    None = 0,

    /// <summary>Обрезать последовательность до допустимой длительности.</summary>
    TrimDuration,

    /// <summary>Ускорить всё, чтобы уложиться в допустимую длительность.</summary>
    SpeedUpToFit,

    /// <summary>Выключить звук.</summary>
    DisableAudio,

    /// <summary>Привести разрешение к требуемому.</summary>
    FixResolution,

    /// <summary>Снизить частоту кадров.</summary>
    FixFrameRate,

    /// <summary>Включить ограничение по размеру файла.</summary>
    LimitFileSize
}

/// <summary>Нарушение требования площадки с предложением, как его убрать.</summary>
public sealed record ConstraintViolation(
    ViolationSeverity Severity,
    string Message,
    AutoFixKind AutoFix);

public sealed record PresetValidationResult(IReadOnlyList<ConstraintViolation> Violations)
{
    public static readonly PresetValidationResult Ok = new([]);

    public bool IsSatisfied => Violations.All(v => v.Severity != ViolationSeverity.Error);

    public bool HasAnything => Violations.Count > 0;
}

/// <summary>
/// Итог применения пресета.
/// </summary>
/// <remarks>
/// Список изменений обязателен: пресет трогает и настройки вывода, и сам монтаж
/// (например, укорачивает последовательность под лимит), а молча переделанный
/// проект — худшее, что может сделать редактор.
/// </remarks>
public sealed record PresetApplyResult(
    Project Project,
    ExportSettings Settings,
    IReadOnlyList<PresetChange> Changes,
    PresetValidationResult Validation);

/// <summary>Как укладываться в лимит длительности.</summary>
public enum DurationFitMode
{
    /// <summary>Обрезать лишнее с конца.</summary>
    Trim = 0,

    /// <summary>Ускорить всё целиком.</summary>
    SpeedUp,

    /// <summary>
    /// Оставить монтаж как есть.
    /// </summary>
    /// <remarks>
    /// Нужен, когда пресет выбирают в списке: параметры вывода должны встать сразу,
    /// а резать чужой монтаж от одного клика по выпадающему списку нельзя.
    /// Превышение лимита при этом никуда не девается — оно попадёт в нарушения.
    /// </remarks>
    Keep
}
