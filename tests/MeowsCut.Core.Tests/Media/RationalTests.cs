using FluentAssertions;
using MeowsCut.Core.Media;

namespace MeowsCut.Core.Tests.Media;

public class RationalTests
{
    [Theory]
    [InlineData("30/1", 30d)]
    [InlineData("30000/1001", 29.97d)]
    [InlineData("24000/1001", 23.976d)]
    [InlineData("25/1", 25d)]
    public void Parse_reads_ffprobe_fractions(string value, double expected)
    {
        Rational.Parse(value).Value.Should().BeApproximately(expected, 0.01);
    }

    [Theory]
    [InlineData("0/0")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("N/A")]
    public void Parse_returns_zero_for_missing_values(string? value)
    {
        // У VFR-видео и части контейнеров поле пустое или 0/0 — это норма, не ошибка.
        Rational.Parse(value).IsZero.Should().BeTrue();
    }

    [Fact]
    public void Parse_accepts_plain_decimal()
    {
        Rational.Parse("29.97").Value.Should().BeApproximately(29.97, 0.01);
    }

    [Fact]
    public void FromDouble_snaps_to_ntsc_fractions()
    {
        Rational.FromDouble(29.97).Should().Be(new Rational(30000, 1001));
        Rational.FromDouble(23.976).Should().Be(new Rational(24000, 1001));
        Rational.FromDouble(60).Should().Be(new Rational(60, 1));
    }
}
