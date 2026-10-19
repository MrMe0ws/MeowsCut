using System.Globalization;

namespace MeowsCut.Ffmpeg.Audio;

/// <summary>Отрезок записи и его громкость — по ним ищется кусок чистого шума.</summary>
public readonly record struct LoudnessWindow(double Start, double End, double RmsDb)
{
    public double Duration => End - Start;
}

/// <summary>
/// Как убрать шум из конкретной записи: где взять образец шума и с каким порогом.
/// </summary>
/// <remarks>
/// Фильтр afftdn с настройками по умолчанию почти не трогает шум громче −50 dBFS,
/// а с жёстким порогом съедает и голос. Поэтому он учится на самой записи: берётся
/// самая тихая полсекунды (у дубля это обычно начало, пока ролик раскачивается),
/// по ней снимается профиль шума, а порог ставится на 10 дБ выше её уровня.
/// На проверке это 17–24 дБ тише шум при потере голоса не больше полутора децибел.
/// </remarks>
public static class NoiseProfile
{
    /// <summary>
    /// Задержка afftdn: его окно сдвигает звук на ~25 мс. Без поправки озвучка
    /// отставала бы от картинки ровно на столько.
    /// </summary>
    public const double FilterDelaySeconds = 0.025;

    /// <summary>Фильтр, который выдаёт громкость каждой половины секунды записи.</summary>
    public const string AnalysisFilter =
        "asetnsamples=n=24000:p=0,astats=metadata=1:reset=1,ametadata=mode=print:key=lavfi.astats.Overall.RMS_level";

    /// <summary>Окно громче этого — уже не тишина, а голос: учиться на нём нельзя.</summary>
    private const double LoudestNoiseDb = -30d;

    /// <summary>Тише этого — цифровая тишина, убирать нечего.</summary>
    private const double SilenceDb = -80d;

    private const double FloorAboveNoiseDb = 10d;
    private const double MinWindowSeconds = 0.2;

    private const string TimeMarker = "pts_time:";
    private const string LevelMarker = "lavfi.astats.Overall.RMS_level=";

    /// <summary>Разбирает вывод <see cref="AnalysisFilter"/>: строки времени и громкости идут парами.</summary>
    public static IReadOnlyList<LoudnessWindow> Parse(IEnumerable<string> lines)
    {
        var starts = new List<double>();
        var levels = new List<double>();

        foreach (var line in lines)
        {
            var time = line.IndexOf(TimeMarker, StringComparison.Ordinal);
            if (time >= 0 &&
                double.TryParse(line[(time + TimeMarker.Length)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var start))
            {
                starts.Add(start);
                continue;
            }

            var level = line.IndexOf(LevelMarker, StringComparison.Ordinal);
            if (level < 0)
            {
                continue;
            }

            var text = line[(level + LevelMarker.Length)..].Trim();
            levels.Add(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var db)
                ? db
                : double.NegativeInfinity);
        }

        var windows = new List<LoudnessWindow>();

        // Последнее окно не берём: его длина неизвестна, и обычно оно обрезано.
        for (var i = 0; i < Math.Min(starts.Count, levels.Count) - 1; i++)
        {
            windows.Add(new LoudnessWindow(starts[i], starts[i + 1], levels[i]));
        }

        return windows;
    }

    /// <summary>
    /// Цепочка фильтров для шумоподавления. null — убирать нечего: запись тихая до нуля.
    /// </summary>
    /// <param name="strengthDb">Насколько приглушить шум.</param>
    public static string? BuildFilter(IReadOnlyList<LoudnessWindow> windows, int strengthDb)
    {
        var full = windows.Where(window => window.Duration >= MinWindowSeconds).ToList();

        // Ровные нули — не шум, а микрофон, который ещё включается: такой кусок бывает
        // в начале каждого дубля. Учиться на нём нельзя, и решать по нему, что запись
        // чистая, — тоже: шум идёт сразу после.
        var usable = full.Where(window => window.RmsDb > SilenceDb).ToList();
        if (full.Count > 0 && usable.Count == 0)
        {
            return null;
        }

        var strength = Math.Clamp(strengthDb, 1, 97).ToString(CultureInfo.InvariantCulture);

        // Запасной вариант, когда учиться не на чем: фильтр подстраивается под шум сам.
        var adaptive = $"afftdn=nr={strength}:nf=-40:tn=1";
        string denoise;

        if (usable.Count == 0)
        {
            denoise = adaptive;
        }
        else
        {
            var quietest = usable.MinBy(window => window.RmsDb);

            if (quietest.RmsDb > LoudestNoiseDb)
            {
                // Тихого места нет — говорили всё время. Профиль по голосу вырезал бы голос.
                denoise = adaptive;
            }
            else
            {
                var floor = Math.Clamp(Math.Round(quietest.RmsDb + FloorAboveNoiseDb), -80d, -20d);
                denoise =
                    $"asendcmd=c='{Seconds(quietest.Start)} afftdn sn start; {Seconds(quietest.End)} afftdn sn stop'," +
                    $"afftdn=nr={strength}:nf={floor.ToString(CultureInfo.InvariantCulture)}";
            }
        }

        // Гул ниже 70 Гц голосу не принадлежит. Хвост дополняется на задержку фильтра,
        // а начало срезается на неё же — длина и время звука остаются прежними.
        var delay = Seconds(FilterDelaySeconds);
        return $"highpass=f=70,apad=pad_dur={delay},{denoise},atrim=start={delay},asetpts=PTS-STARTPTS";
    }

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
