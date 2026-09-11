using FluentAssertions;
using MeowsCut.App.Formatting;

namespace MeowsCut.App.Tests.Formatting;

/// <summary>
/// Шкала ползунка скорости и разбор набранного вручную числа.
/// </summary>
public class SpeedScaleTests
{
    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(16)]
    public void Slider_and_speed_agree(double speed)
    {
        SpeedScale.FromSlider(SpeedScale.ToSlider(speed)).Should().BeApproximately(speed, 0.01);
    }

    [Fact]
    public void Doubling_the_speed_always_costs_the_same_travel()
    {
        // Ради этого шкала и логарифмическая: на линейной всё замедление
        // уместилось бы в первые проценты хода ползунка.
        var slow = SpeedScale.ToSlider(2) - SpeedScale.ToSlider(1);
        var fast = SpeedScale.ToSlider(16) - SpeedScale.ToSlider(8);

        slow.Should().BeApproximately(fast, 0.0001);
    }

    [Fact]
    public void Slider_snaps_to_the_usual_speed()
    {
        // Попасть мышью ровно в 1× иначе почти невозможно, а нужно это чаще всего.
        var nearOne = SpeedScale.ToSlider(1) + 0.02;

        SpeedScale.FromSlider(nearOne).Should().Be(1d);
    }

    [Fact]
    public void Slider_keeps_two_decimals()
    {
        var speed = SpeedScale.FromSlider(SpeedScale.ToSlider(3.14159));

        speed.Should().Be(Math.Round(speed, 2));
    }

    [Fact]
    public void Slider_stays_inside_the_limits()
    {
        SpeedScale.FromSlider(SpeedScale.Max).Should().BeLessThanOrEqualTo(16d);
        SpeedScale.FromSlider(SpeedScale.Min).Should().BeGreaterThanOrEqualTo(0.1);
    }

    [Theory]
    [InlineData("1.15", 1.15)]
    [InlineData("2,35", 2.35)]
    [InlineData(" 0.5 ", 0.5)]
    public void Typed_numbers_are_understood(string text, double expected)
    {
        // Запятая — потому что на цифровом блоке именно она, и помнить
        // про точку пользователь не обязан.
        SpeedScale.TryParse(text, out var speed).Should().BeTrue();
        speed.Should().BeApproximately(expected, 0.0001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0.")]
    [InlineData("быстро")]
    [InlineData("0.05")]
    [InlineData("40")]
    public void Nonsense_and_out_of_range_are_refused(string text)
    {
        // Незаконченный ввод не должен дёргать скорость на каждую букву.
        SpeedScale.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(1.5, "1.5")]
    [InlineData(2.35, "2.35")]
    public void Speed_is_shown_without_trailing_zeros(double speed, string expected)
    {
        SpeedScale.Format(speed).Should().Be(expected);
    }
}
