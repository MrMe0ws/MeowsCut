using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using FluentAssertions;
using MeowsCut.App.Controls;

namespace MeowsCut.App.Tests.Views;

/// <summary>Число рядом с ползунком: набрать, покрутить колёсиком, не испортить чужой кусок.</summary>
[Collection("WPF")]
public class NumberBoxTests
{
    private static NumberBox Box(double value = 100) =>
        new() { Minimum = 0, Maximum = 200, Step = 1, Format = "0", Suffix = "%", Value = value };

    private static void Wheel(NumberBox box, int delta, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            box.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = UIElement.MouseWheelEvent });
        }
    }

    [Fact]
    public void Shows_the_value_with_its_unit() => WpfRunner.Run(() =>
    {
        Box(37).Text.Should().Be("37%");
    });

    [Fact]
    public void Wheel_moves_by_one_step() => WpfRunner.Run(() =>
    {
        var box = Box(100);

        Wheel(box, 120, times: 3);
        box.Value.Should().Be(103, "громкость 5%-шагами и была жалобой");

        Wheel(box, -120);
        box.Value.Should().Be(102);
        box.Text.Should().Be("102%");
    });

    [Fact]
    public void Wheel_stops_at_the_limit() => WpfRunner.Run(() =>
    {
        var box = Box(199);

        Wheel(box, 120, times: 5);

        box.Value.Should().Be(200);
    });

    [Fact]
    public void Typed_number_applies_on_enter_and_ends_the_edit() => WpfRunner.Run(() =>
    {
        var ended = 0;
        var box = Box(100);
        box.EndCommand = new RelayCommand(() => ended++);

        box.Text = "37,6";
        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Enter)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent
        });

        box.Value.Should().Be(38);
        box.Text.Should().Be("38%");
        ended.Should().Be(1, "иначе правка склеилась бы в истории со следующей");
    });

    [Fact]
    public void New_selection_is_not_overwritten_by_the_old_text() => WpfRunner.Run(() =>
    {
        // Выделили другой кусок: модель сменила значение. Поле не набирали —
        // значит, оно обязано показать новое и ничего не записать обратно.
        var box = Box(100);

        box.Value = 40;
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null)
        {
            RoutedEvent = UIElement.LostKeyboardFocusEvent
        });

        box.Value.Should().Be(40);
        box.Text.Should().Be("40%");
    });

    private sealed class FakeSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;

        public override bool IsDisposed => false;

        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
