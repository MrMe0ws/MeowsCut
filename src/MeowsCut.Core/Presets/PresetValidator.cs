using MeowsCut.Core.Editing;
using MeowsCut.Core.Export;

namespace MeowsCut.Core.Presets;

/// <summary>
/// Проверяет требования площадки по текущему проекту и настройкам вывода.
/// </summary>
/// <remarks>
/// Работает независимо от применения пресета: пользователь вправе поправить настройки
/// руками, и проверка обязана продолжать предупреждать, а не молчать до экспорта.
/// </remarks>
public sealed class PresetValidator : IPresetValidator
{
    public PresetValidationResult Validate(PresetConstraints constraints, Project project, ExportSettings settings)
    {
        var violations = new List<ConstraintViolation>();
        var sequence = project.Sequence;

        if (constraints.MaxDuration is { } maxDuration && sequence.Duration > maxDuration)
        {
            violations.Add(new ConstraintViolation(
                ViolationSeverity.Error,
                $"Длительность {sequence.Duration.TotalSeconds:0.#} с — допустимо не больше {maxDuration.TotalSeconds:0.#} с",
                AutoFixKind.TrimDuration));
        }

        var resolution = settings.Video.Resolution.Resolve(sequence.Format.Size);

        if (constraints.RequireSquare && !resolution.IsSquare)
        {
            violations.Add(new ConstraintViolation(
                ViolationSeverity.Error,
                $"Кадр должен быть квадратным, сейчас {resolution}",
                AutoFixKind.FixResolution));
        }

        if (constraints.MaxWidth is { } maxWidth && resolution.Width > maxWidth)
        {
            violations.Add(new ConstraintViolation(
                ViolationSeverity.Error,
                $"Ширина {resolution.Width} — допустимо не больше {maxWidth}",
                AutoFixKind.FixResolution));
        }

        if (constraints.MaxHeight is { } maxHeight && resolution.Height > maxHeight)
        {
            violations.Add(new ConstraintViolation(
                ViolationSeverity.Error,
                $"Высота {resolution.Height} — допустимо не больше {maxHeight}",
                AutoFixKind.FixResolution));
        }

        var fps = settings.Video.FrameRate.Effective(sequence.Format.FrameRate);
        if (constraints.MaxFps is { } maxFps && fps > maxFps + 0.01)
        {
            violations.Add(new ConstraintViolation(
                ViolationSeverity.Error,
                $"Частота кадров {fps:0.##} — допустимо не больше {maxFps:0.##}",
                AutoFixKind.FixFrameRate));
        }

        if (constraints.ForbidAudio && settings.Audio.Enabled && sequence.HasAudio)
        {
            violations.Add(new ConstraintViolation(
                ViolationSeverity.Error,
                "Звук здесь не воспроизводится и должен быть удалён",
                AutoFixKind.DisableAudio));
        }

        if (constraints.AllowedContainers is { Count: > 0 } containers &&
            !containers.Contains(settings.Container))
        {
            violations.Add(new ConstraintViolation(
                ViolationSeverity.Error,
                $"Формат {settings.Container} не подходит: нужен {string.Join(" или ", containers)}",
                AutoFixKind.None));
        }

        if (constraints.AllowedVideoCodecs is { Count: > 0 } codecs &&
            !codecs.Contains(settings.Video.Codec))
        {
            violations.Add(new ConstraintViolation(
                ViolationSeverity.Error,
                $"Кодек {settings.Video.Codec.DisplayName()} не подходит: нужен " +
                string.Join(" или ", codecs.Select(c => c.DisplayName())),
                AutoFixKind.None));
        }

        ValidateFileSize(constraints, settings, violations);

        return new PresetValidationResult(violations);
    }

    /// <summary>
    /// Размер проверяется по режиму кодирования, а не по факту: файла ещё нет.
    /// Поэтому «нет ограничения размера» — это предупреждение, а не ошибка.
    /// </summary>
    private static void ValidateFileSize(
        PresetConstraints constraints,
        ExportSettings settings,
        List<ConstraintViolation> violations)
    {
        if (constraints.MaxFileSizeBytes is not { } limit)
        {
            return;
        }

        switch (settings.Video.RateControl)
        {
            case RateControl.TargetSize target when target.Bytes > limit:
                violations.Add(new ConstraintViolation(
                    ViolationSeverity.Error,
                    $"Ограничение размера {target.Bytes / 1024} КБ больше допустимых {limit / 1024} КБ",
                    AutoFixKind.LimitFileSize));
                break;

            case RateControl.TargetSize:
                break;

            default:
                violations.Add(new ConstraintViolation(
                    ViolationSeverity.Warning,
                    $"Размер файла ничем не ограничен, а нужно уложиться в {limit / 1024} КБ",
                    AutoFixKind.LimitFileSize));
                break;
        }
    }
}
