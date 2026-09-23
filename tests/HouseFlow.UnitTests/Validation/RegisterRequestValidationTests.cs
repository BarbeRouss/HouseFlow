using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using HouseFlow.Application.DTOs;

namespace HouseFlow.UnitTests.Validation;

/// <summary>
/// Password policy (#156): at least 8 characters, with an uppercase letter, a lowercase
/// letter, a digit and a special character. Enforced by the RegularExpression/StringLength
/// attributes NSwag generates onto RegisterRequestDto from specs/openapi.yaml.
/// </summary>
public class RegisterRequestValidationTests
{
    private static RegisterRequestDto Request(string password) =>
        new(firstName: "Test", lastName: "User", email: "test@example.com", password: password);

    private static bool IsPasswordValid(string password)
    {
        var request = Request(password);
        var results = new List<ValidationResult>();
        return Validator.TryValidateProperty(
            request.Password, new ValidationContext(request) { MemberName = nameof(RegisterRequestDto.Password) }, results);
    }

    [Theory]
    [InlineData("Sh1!aaa")]            // 7 characters: too short
    [InlineData("alllowercase123!")]   // no uppercase
    [InlineData("ALLUPPERCASE123!")]   // no lowercase
    [InlineData("NoDigitsHere!!")]     // no digit
    [InlineData("NoSpecialChar123")]   // no special character
    public void Password_ViolatingPolicy_IsRejected(string password)
    {
        IsPasswordValid(password).Should().BeFalse();
    }

    [Theory]
    [InlineData("Pass123!")]           // 8 characters: exactly at the minimum
    [InlineData("Password123!")]
    [InlineData("MonMotDePasse123!")]
    [InlineData("Sup3r-Str0ng_Pass")]
    public void Password_MeetingPolicy_IsAccepted(string password)
    {
        IsPasswordValid(password).Should().BeTrue();
    }
}
