using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Contracts;
using HouseFlow.Core;

namespace HouseFlow.UnitTests.Common;

public class HouseColorsTests
{
    [Fact]
    public void Palette_FollowsTheUxReadmeOrder()
    {
        HouseColors.Palette.Should().Equal("indigo", "orange", "green", "sky", "yellow", "pink");
        HouseColors.Default.Should().Be("indigo");
        HouseColors.Palette.Should().OnlyContain(k => k.Length <= HouseColors.MaxLength);
    }

    [Fact]
    public void Next_WithoutHouses_IsTheFirstColour()
    {
        HouseColors.Next([]).Should().Be("indigo");
    }

    [Fact]
    public void Next_RotatesThroughThePaletteThenWrapsAround()
    {
        var keys = new List<string>();
        for (var i = 0; i < 8; i++) keys.Add(HouseColors.Next(keys));

        keys.Should().Equal("indigo", "orange", "green", "sky", "yellow", "pink", "indigo", "orange");
    }

    [Fact]
    public void Next_ReusesAColourFreedByADeletion()
    {
        // orange was deleted: it is the least used colour again.
        HouseColors.Next(["indigo", "green"]).Should().Be("orange");
    }

    [Fact]
    public void Next_PicksTheLeastUsedColour_WhenTheOwnerChoseColoursManually()
    {
        HouseColors.Next(["indigo", "indigo", "orange", "green", "sky", "yellow", "pink"]).Should().Be("orange");
    }

    [Fact]
    public void Next_IgnoresUnknownKeys()
    {
        HouseColors.Next(["purple", "indigo"]).Should().Be("orange");
    }

    [Theory]
    [InlineData("indigo", true)]
    [InlineData("pink", true)]
    [InlineData("Indigo", false)]
    [InlineData("purple", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_AcceptsOnlyPaletteKeys(string? key, bool expected)
    {
        HouseColors.IsValid(key).Should().Be(expected);
    }

    [Fact]
    public void GeneratedEnum_MapsOneToOneOntoThePalette()
    {
        // Guards against the OpenAPI enum (HouseColorKey) drifting from the stored palette.
        Enum.GetValues<HouseColorKey>().Select(HouseColorKeys.ToKey).Should().Equal(HouseColors.Palette);
    }

    [Fact]
    public void IsDefined_RejectsOutOfRangeValues()
    {
        HouseColorKeys.IsDefined(null).Should().BeTrue();
        HouseColorKeys.IsDefined(HouseColorKey.Sky).Should().BeTrue();
        HouseColorKeys.IsDefined((HouseColorKey)42).Should().BeFalse();
    }
}
