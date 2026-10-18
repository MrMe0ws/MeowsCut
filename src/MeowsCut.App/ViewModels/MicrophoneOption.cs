using System.Globalization;
using MeowsCut.App.Localization;
using MeowsCut.Core.Abstractions;

namespace MeowsCut.App.ViewModels;

/// <summary>Пункт списка микрофонов. Id null — «системный по умолчанию».</summary>
public sealed record MicrophoneOption(string? Id, string Title)
{
    public static MicrophoneOption SystemDefault { get; } = new(null, Strings.MicrophoneDefault);

    /// <summary>
    /// Список для настроек. Первым идёт системный: его выбирают, чтобы программа
    /// шла за Windows, когда гарнитуру то подключают, то нет. Выбранный раньше,
    /// но сейчас отключённый микрофон остаётся в списке — иначе настройка
    /// молча сбросилась бы, стоило выдернуть гарнитуру до открытия окна.
    /// </summary>
    public static IReadOnlyList<MicrophoneOption> Build(IReadOnlyList<AudioInputDevice> devices, string? selectedId)
    {
        var options = new List<MicrophoneOption> { SystemDefault };

        foreach (var device in devices)
        {
            var title = device.IsDefault
                ? string.Format(CultureInfo.CurrentUICulture, Strings.MicrophoneDefaultSuffix, device.Name)
                : device.Name;

            options.Add(new MicrophoneOption(device.Id, title));
        }

        if (selectedId is not null && options.All(option => option.Id != selectedId))
        {
            options.Add(new MicrophoneOption(selectedId, selectedId));
        }

        return options;
    }
}
