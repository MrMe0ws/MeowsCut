using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MeowsCut.App.ViewModels;

/// <summary>
/// Готовое значение параметра одним нажатием: «0,5×», «2×».
/// </summary>
/// <remarks>
/// Чип — короткий путь, а не ограничение: рядом с каждым рядом чипов стоит поле,
/// в которое вписывают любое своё число, и ползунок. Текущее значение подсвечено,
/// даже если его набрали руками, — так видно, что «2» в поле и «2×» на чипе одно и то же.
/// </remarks>
public sealed partial class ChoiceChip(string title, double value, Action<double> pick) : ObservableObject
{
    public string Title { get; } = title;

    public double Value { get; } = value;

    [ObservableProperty]
    private bool _isActive;

    [RelayCommand]
    private void Pick() => pick(Value);

    /// <summary>Подсвечивает чип, совпадающий с текущим значением.</summary>
    public static void MarkActive(IEnumerable<ChoiceChip> chips, double current, double tolerance)
    {
        foreach (var chip in chips)
        {
            chip.IsActive = Math.Abs(chip.Value - current) < tolerance;
        }
    }
}
