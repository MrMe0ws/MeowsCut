using System.Globalization;
using MeowsCut.Core.Editing.Timeline;

namespace MeowsCut.Ffmpeg.Arguments;

/// <summary>
/// Кадрирование куска: поворот, масштаб, сдвиг и наезд.
/// </summary>
/// <remarks>
/// Неподвижный кадр — обрезка и масштаб: сначала вырезается видимая часть,
/// потом растягивается. Наоборот нельзя — 2000% от фотографии 4000 px потребовали
/// бы кадр в 80 000 px, такого swscale не делает.
///
/// Наезд — perspective с пересчётом на каждом кадре. Он берёт окно с точностью
/// до доли пикселя, поэтому медленный наезд не дрожит, как у zoompan, где окно
/// прыгает на целый пиксель исходника. Формула та же, что в
/// <see cref="ClipTransform.At"/>: предпросмотр и файл едут одинаково.
/// </remarks>
public static class TransformFilter
{
    /// <param name="frameRate">Частота кадров ролика: наезд считается по номеру кадра.</param>
    public static void Append(List<string> chain, ClipTransform transform, TimeSpan clipDuration, string frameRate)
    {
        if (transform.IsIdentity)
        {
            return;
        }

        // Поворот идёт первым: масштаб и смещение пользователь задаёт, глядя
        // на уже развёрнутый кадр, и применять их к исходной ориентации значило бы
        // двигать картинку не в ту сторону.
        AppendRotation(chain, transform.Rotation);

        if (transform.HasMotion)
        {
            AppendMotion(chain, transform, clipDuration, frameRate);
            return;
        }

        var still = transform.Clamped();

        // Повёрнутый или подогнанный по-своему кадр без масштаба и сдвига: пустая пара
        // scale и crop стоила бы перемасштабирования каждого кадра впустую.
        if (still.IsUnscaled)
        {
            return;
        }

        var zoom = Number(still.Zoom);
        var dx = Number(still.OffsetX);
        var dy = Number(still.OffsetY);

        if (still.Zoom >= 1d)
        {
            chain.Add($"crop=w=iw/{zoom}:h=ih/{zoom}:x=(iw-ow)/2+ow*{dx}:y=(ih-oh)/2+oh*{dy}");
            chain.Add($"scale=iw*{zoom}:ih*{zoom}");
        }
        else
        {
            // Знак как у обрезки и в предпросмотре: положительный сдвиг уводит
            // картинку влево и вверх. С плюсом уменьшенный кадр ехал в обратную сторону.
            chain.Add($"pad=w=iw/{zoom}:h=ih/{zoom}:x=(ow-iw)/2-ow*{dx}:y=(oh-ih)/2-oh*{dy}:color=black");
        }
    }

    /// <summary>
    /// Наезд. Окно ездит по холсту: при увеличении это сама картинка, при отъезде
    /// ниже 100% — картинка в чёрных полях, иначе perspective размазал бы края.
    /// </summary>
    /// <remarks>
    /// Холст после этого больше исходника, если были поля, — его возвращает
    /// к кадру ролика общее приведение размера (<see cref="Sequence.HasFraming"/>).
    /// </remarks>
    private static void AppendMotion(List<string> chain, ClipTransform transform, TimeSpan clipDuration, string frameRate)
    {
        // Наезд считается по номеру кадра — другого времени perspective не знает.
        // Поэтому частота кадров перед ним ровная.
        chain.Add($"fps={frameRate}");

        var canvas = 1d;
        if (transform.Zoom < 1d)
        {
            canvas = 1d / transform.Zoom;
            var k = Number(canvas);
            chain.Add($"pad=w=ceil(iw*{k}/2)*2:h=ceil(ih*{k}/2)*2:x=(ow-iw)/2:y=(oh-ih)/2:color=black");
        }

        var frames = Number(Math.Max(1d, Math.Min(transform.MotionSeconds, clipDuration.TotalSeconds) * Rate(frameRate)));

        // ld(0) — доля пути со сглаживанием, ld(1) — масштаб окна относительно холста.
        var prelude =
            $"st(0,clip(in/{frames},0,1));st(0,ld(0)*ld(0)*(3-2*ld(0)));" +
            (transform.MotionOut ? "st(0,1-ld(0));" : string.Empty) +
            $"st(1,{Number(canvas)}*pow({Number(transform.Zoom)},ld(0)));";

        var left = Edge("W", transform.OffsetX);
        var top = Edge("H", transform.OffsetY);

        chain.Add(
            "perspective=" +
            $"x0='{prelude}{left}':y0='{prelude}{top}':" +
            $"x1='{prelude}{left}+W/ld(1)':y1='{prelude}{top}':" +
            $"x2='{prelude}{left}':y2='{prelude}{top}+H/ld(1)':" +
            $"x3='{prelude}{left}+W/ld(1)':y3='{prelude}{top}+H/ld(1)':" +
            "interpolation=cubic:eval=frame");
    }

    /// <summary>
    /// Левый (верхний) край окна: от середины холста со сдвигом в долях окна,
    /// но не дальше краёв — так же ужимает сдвиг <see cref="ClipTransform.Clamped"/>.
    /// </summary>
    private static string Edge(string size, double offset) =>
        $"clip({size}/2+{size}/ld(1)*({Number(offset)}*ld(0)-0.5),0,{size}-{size}/ld(1))";

    /// <summary>
    /// Поворот на прямой угол. transpose работает без потерь и без интерполяции,
    /// в отличие от rotate с произвольным углом.
    /// </summary>
    private static void AppendRotation(List<string> chain, int rotation)
    {
        switch (rotation)
        {
            case 90:
                chain.Add("transpose=1");
                break;

            case 180:
                // Отражение по обеим осям дешевле двух transpose подряд
                // и не требует дважды переписывать кадр в памяти.
                chain.Add("hflip");
                chain.Add("vflip");
                break;

            case 270:
                chain.Add("transpose=2");
                break;
        }
    }

    private static double Rate(string frameRate)
    {
        var parts = frameRate.Split('/');
        var numerator = double.Parse(parts[0], CultureInfo.InvariantCulture);
        return parts.Length == 2 ? numerator / double.Parse(parts[1], CultureInfo.InvariantCulture) : numerator;
    }

    private static string Number(double value) => SpeedFilter.FormatFactor(value);
}
