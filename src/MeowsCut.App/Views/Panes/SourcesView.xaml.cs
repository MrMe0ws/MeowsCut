using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MeowsCut.App.Views.Panes;

/// <summary>Файлы, добавленные в проект.</summary>
/// <remarks>
/// Высоту раздела «Звук» тянут за его верхнюю границу. Пока её не трогали,
/// раздел растёт по списку, но не выше заводского предела; после — держит
/// выставленную высоту, и раскладка запоминает её до следующего запуска.
/// </remarks>
public partial class SourcesView : UserControl
{
    /// <summary>Меньше — не помещаются заголовок и одна строка файла.</summary>
    private const double MinSoundHeight = 84;

    /// <summary>Сколько сетке обложек остаётся при самом высоком разделе звука.</summary>
    private const double MinCoversHeight = 150;

    /// <summary>Заводской предел: раздел растёт по списку, но не выше этого.</summary>
    private const double DefaultMaxSoundHeight = 190;

    private double _soundHeight;

    public SourcesView()
    {
        InitializeComponent();
        ApplySoundHeight();
        SizeChanged += (_, _) => ApplySoundHeight();
    }

    /// <summary>Высоту раздела «Звук» отпустили — пора записать раскладку.</summary>
    public event EventHandler? SoundHeightCommitted;

    /// <summary>Высота раздела «Звук», выставленная руками. 0 — заводская, по списку.</summary>
    public double SoundHeight
    {
        get => _soundHeight;
        set
        {
            _soundHeight = Math.Max(0, value);
            ApplySoundHeight();
        }
    }

    private double MostSoundHeight => Math.Max(MinSoundHeight, ActualHeight - MinCoversHeight);

    private void OnSoundResize(object sender, DragDeltaEventArgs e)
    {
        // Граница сверху: тянут вверх — раздел растёт.
        var current = _soundHeight > 0 ? _soundHeight : SoundSection.ActualHeight;
        SoundHeight = Math.Clamp(current - e.VerticalChange, MinSoundHeight, MostSoundHeight);
    }

    private void OnSoundResizeCompleted(object sender, DragCompletedEventArgs e) =>
        SoundHeightCommitted?.Invoke(this, EventArgs.Empty);

    private void ApplySoundHeight()
    {
        if (_soundHeight <= 0)
        {
            SoundSection.ClearValue(HeightProperty);
            SoundSection.MaxHeight = DefaultMaxSoundHeight;
            return;
        }

        SoundSection.MaxHeight = double.PositiveInfinity;
        SoundSection.Height = ActualHeight > 0
            ? Math.Clamp(_soundHeight, MinSoundHeight, MostSoundHeight)
            : _soundHeight;
    }
}
