using FluentAssertions;
using HouseFlow.Application.Common;

namespace HouseFlow.UnitTests.Validation;

public class NotInFutureAttributeTests
{
    private readonly NotInFutureAttribute _attribute = new();

    [Fact]
    public void IsValid_WithPastDate_ReturnsTrue()
    {
        _attribute.IsValid(DateTime.UtcNow.AddDays(-1)).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithCurrentDate_ReturnsTrue()
    {
        _attribute.IsValid(DateTime.UtcNow).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithFutureDate_ReturnsFalse()
    {
        _attribute.IsValid(DateTime.UtcNow.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithNull_ReturnsTrue()
    {
        _attribute.IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithFarFutureDate_ReturnsFalse()
    {
        _attribute.IsValid(DateTime.UtcNow.AddYears(1)).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithFarPastDate_ReturnsTrue()
    {
        _attribute.IsValid(DateTime.UtcNow.AddYears(-10)).Should().BeTrue();
    }
}
