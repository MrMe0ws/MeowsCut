using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MeowsCut.Core.Abstractions;

namespace MeowsCut.App.Views.Dialogs;

/// <summary>
/// Настройки поиска пауз перед запуском.
/// </summary>
/// <remarks>
/// Своя маленькая модель прямо здесь, без отдельного ViewModel: окно живёт
/// ровно до нажатия кнопки и ничего, кроме трёх чисел, не хранит.
/// </remarks>
public partial class SilenceWindow : Window, INotifyPropertyChanged
{
    private double _thresholdDb;
    private double _minimumSeconds;
    private double _paddingMilliseconds;

    public SilenceWindow(SilenceOptions options)
    {
        _thresholdDb = options.ThresholdDb;
        _minimumSeconds = options.EffectiveMinDuration.TotalSeconds;
        _paddingMilliseconds = options.Padding.TotalMilliseconds;

        DataContext = this;
        InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double ThresholdDb
    {
        get => _thresholdDb;
        set => Set(ref _thresholdDb, value);
    }

    public double MinimumSeconds
    {
        get => _minimumSeconds;
        set => Set(ref _minimumSeconds, value);
    }

    public double PaddingMilliseconds
    {
        get => _paddingMilliseconds;
        set => Set(ref _paddingMilliseconds, value);
    }

    /// <summary>Что выбрал пользователь. Читается после закрытия окна.</summary>
    public SilenceOptions Result => new(ThresholdDb, TimeSpan.FromSeconds(MinimumSeconds))
    {
        Padding = TimeSpan.FromMilliseconds(PaddingMilliseconds)
    };

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Set(ref double field, double value, [CallerMemberName] string? property = null)
    {
        if (Math.Abs(field - value) < 0.0001)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
