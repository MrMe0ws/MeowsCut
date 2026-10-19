using FluentAssertions;
using MeowsCut.App.Formatting;

namespace MeowsCut.App.Tests.Formatting;

/// <summary>Число, набранное в поле рядом с ползунком.</summary>
public class NumberTextTests
{
    [Theory]
    [InlineData("37", 37d)]
    [InlineData("37%", 37d)]
    [InlineData(" 0,4 с", 0.4d)]
    [InlineData("0.4", 0.4d)]
    [InlineData("-12 пт", -12d)]
    [InlineData("+3", 3d)]
    [InlineData("−5", -5d)]
    public void Reads_what_people_type(string text, double expected)
    {
        NumberText.Parse(text).Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("громко")]
    [InlineData("%")]
    public void Not_a_number_changes_nothing(string text)
    {
        NumberText.Parse(text).Should().BeNull();
    }

    [Fact]
    public void Keeps_within_the_limits()
    {
        NumberText.Fit(250, 0, 200, "0").Should().Be(200);
        NumberText.Fit(-3, 0, 200, "0").Should().Be(0);
    }

    [Fact]
    public void Rounds_to_what_is_shown()
    {
        NumberText.Fit(37.4999, 0, 200, "0").Should().Be(37, "иначе на экране 37, а в модели 37.4999");
        NumberText.Fit(0.44, 0, 5, "0.0").Should().Be(0.4);
    }

    [Fact]
    public void Signed_format_survives_the_rounding()
    {
        NumberText.Fit(3.2, -12, 12, "+#;-#;0").Should().Be(3);
        NumberText.Fit(-7.6, -12, 12, "+#;-#;0").Should().Be(-8);
    }
}
