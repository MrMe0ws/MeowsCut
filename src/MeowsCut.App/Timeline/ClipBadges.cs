using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.App.Timeline;

/// <summary>Значок на куске: что к нему применено сверх умолчания.</summary>
public enum ClipBadge
{
    Speed = 0,

    /// <summary>Звук куска выключен руками.</summary>
    Muted,

    /// <summary>Громкость сдвинута, но звук остался.</summary>
    Volume,

    Pitch,
    Rotation,

    /// <summary>Кадр приближен или сдвинут.</summary>
    Framing
}

/// <summary>
/// Какие значки показать на куске доски.
/// </summary>
/// <remarks>
/// Отдельно от отрисовки: решение «что показать» проверяется без окна, а контрол
/// только переводит значки в картинки. Правило одно — значок появляется, лишь
/// когда свойство отличается от умолчания. Показывать всё подряд нельзя: лента
/// станет пёстрой и перестанет отвечать на вопрос, ради которого затевалась, —
/// какой из кусков тронут, а какой нет.
/// </remarks>
public static class ClipBadges
{
    /// <summary>
    /// Порядок значков закреплён здесь и не зависит от того, что применили раньше:
    /// глаз привыкает к месту, и «повёрнут ли этот кусок» видно, не перечитывая ряд.
    /// </summary>
    public static IReadOnlyList<ClipBadge> For(Clip clip)
    {
        var badges = new List<ClipBadge>(4);

        if (clip.IsSpeedChanged)
        {
            badges.Add(ClipBadge.Speed);
        }

        // У фотографии звука нет вовсе, и «без звука» на ней означало бы
        // применённую правку там, где её не делали.
        if (clip.SourceHasAudio)
        {
            if (!clip.Audio.Enabled)
            {
                badges.Add(ClipBadge.Muted);
            }
            else if (Math.Abs(clip.Audio.Volume - 1d) > 0.0001)
            {
                badges.Add(ClipBadge.Volume);
            }
        }

        if (clip.Transform.IsRotated)
        {
            badges.Add(ClipBadge.Rotation);
        }

        // Поворот показан своим значком, поэтому здесь только приближение и сдвиг.
        if (Math.Abs(clip.Transform.Zoom - 1d) > 0.0001 ||
            Math.Abs(clip.Transform.OffsetX) > 0.0001 ||
            Math.Abs(clip.Transform.OffsetY) > 0.0001)
        {
            badges.Add(ClipBadge.Framing);
        }

        return badges;
    }

    public static IReadOnlyList<ClipBadge> For(AudioClip clip)
    {
        var badges = new List<ClipBadge>(3);

        if (clip.IsSpeedChanged)
        {
            badges.Add(ClipBadge.Speed);
        }

        // Тишина дорожки уже написана рядом с её именем; на каждом куске
        // повторять её незачем.
        if (clip.IsGainChanged)
        {
            badges.Add(ClipBadge.Volume);
        }

        if (clip.IsPitchShifted)
        {
            badges.Add(ClipBadge.Pitch);
        }

        return badges;
    }
}
