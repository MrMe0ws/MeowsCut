using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MeowsCut.App.Formatting;

namespace MeowsCut.App.Controls;

/// <summary>
/// Число рядом с ползунком, которое можно не только читать, но и править:
/// набрать руками, покрутить колёсиком, подвинуть стрелками.
/// </summary>
/// <remarks>
/// Ползунок хорош для грубого хода, но точное значение — 37% громкости, 0.4 с
/// затухания — им приходится ловить по пикселю. Поле правит то же свойство,
/// что и ползунок, поэтому они всегда показывают одно и то же.
///
/// Колёсико: шаг <see cref="Step"/>, с Shift — вдесятеро больше. Набранное
/// применяется по Enter или когда поле теряет фокус; Esc возвращает прежнее.
/// </remarks>
public sealed class NumberBox : TextBox
{
    /// <summary>Пауза после последнего щелчка колёсика, после которой правка считается законченной.</summary>
    private static readonly TimeSpan WheelSettle = TimeSpan.FromMilliseconds(600);

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumberBox),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDisplayChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(NumberBox), new PropertyMetadata(0d));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(NumberBox), new PropertyMetadata(100d));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(NumberBox), new PropertyMetadata(1d));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(NumberBox), new PropertyMetadata("0", OnDisplayChanged));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(NumberBox), new PropertyMetadata(string.Empty, OnDisplayChanged));

    /// <summary>Правка закончена — модель закрывает склейку правок в истории.</summary>
    public static readonly DependencyProperty EndCommandProperty = DependencyProperty.Register(
        nameof(EndCommand), typeof(ICommand), typeof(NumberBox), new PropertyMetadata(null));

    private readonly DispatcherTimer _wheelSettle;

    /// <summary>Текст набран руками и ещё не применён.</summary>
    private bool _edited;
    private bool _showing;

    public NumberBox()
    {
        _wheelSettle = new DispatcherTimer { Interval = WheelSettle };
        _wheelSettle.Tick += (_, _) =>
        {
            _wheelSettle.Stop();
            FireEnd();
        };

        ShowValue();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public ICommand? EndCommand
    {
        get => (ICommand?)GetValue(EndCommandProperty);
        set => SetValue(EndCommandProperty, value);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        if (!_showing)
        {
            _edited = true;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // Первый щелчок выделяет число целиком: его почти всегда набирают заново,
        // а не правят по одной цифре.
        if (!IsKeyboardFocusWithin)
        {
            Focus();
            e.Handled = true;
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SelectAll();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Commit();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Commit();
                SelectAll();
                e.Handled = true;
                break;
            case Key.Escape:
                ShowValue();
                SelectAll();
                e.Handled = true;
                break;
            case Key.Up:
                Nudge(+1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                FireEnd();
                e.Handled = true;
                break;
            case Key.Down:
                Nudge(-1, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                FireEnd();
                e.Handled = true;
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!IsEnabled || e.Delta == 0)
        {
            return;
        }

        // Колёсико над полем меняет число, а не прокручивает панель: иначе
        // панель уезжала бы из-под курсора на первом же щелчке.
        e.Handled = true;
        Nudge(Math.Sign(e.Delta), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));

        _wheelSettle.Stop();
        _wheelSettle.Start();
    }

    private void Nudge(int direction, bool coarse)
    {
        var current = _edited && NumberText.Parse(Text) is { } typed ? typed : Value;
        var step = Step * (coarse ? 10 : 1);

        // Ход по сетке шага: с 37% колёсико ведёт на 38, а не на 37.000001.
        var next = Math.Round(current / step + direction, MidpointRounding.AwayFromZero) * step;
        SetClamped(next);
        ShowValue();

        if (IsKeyboardFocusWithin)
        {
            SelectAll();
        }
    }

    private void Commit()
    {
        // Применяется только набранное. Иначе щелчок по другому куску на доске
        // сначала менял выделение, а потом поле, теряя фокус, записывало старое
        // число уже в новый кусок.
        if (_edited && NumberText.Parse(Text) is { } typed)
        {
            var changed = Math.Abs(typed - Value) > double.Epsilon;
            SetClamped(typed);
            if (changed)
            {
                FireEnd();
            }
        }

        ShowValue();
    }

    private void SetClamped(double value) => Value = NumberText.Fit(value, Minimum, Maximum, Format);

    private void ShowValue()
    {
        _showing = true;
        try
        {
            Text = Value.ToString(Format, Language.GetSpecificCulture()) + Suffix;
            _edited = false;
        }
        finally
        {
            _showing = false;
        }
    }

    private void FireEnd()
    {
        if (EndCommand is { } command && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private static void OnDisplayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Пока в поле набирают, внешнее значение текст не перебивает.
        if (d is NumberBox box && !box._edited)
        {
            box.ShowValue();
        }
    }
}
