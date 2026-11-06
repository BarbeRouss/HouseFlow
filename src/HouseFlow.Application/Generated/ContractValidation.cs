using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Contracts;

/// <summary>
/// Partial class extensions to add validation attributes that NSwag doesn't generate
/// from OpenAPI format hints (e.g., format: email).
/// </summary>
public partial class RegisterRequest : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(Email) && !new EmailAddressAttribute().IsValid(Email))
        {
            yield return new ValidationResult("Invalid email format", new[] { nameof(Email) });
        }
    }
}

public partial class LoginRequest : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(Email) && !new EmailAddressAttribute().IsValid(Email))
        {
            yield return new ValidationResult("Invalid email format", new[] { nameof(Email) });
        }
    }
}

// The global JsonStringEnumConverter also accepts integers: reject values outside the palette (e.g. 42).
public partial class CreateHouseRequest : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!HouseFlow.Application.Common.HouseColorKeys.IsDefined(ColorKey))
            yield return new ValidationResult("Invalid house colour", new[] { nameof(ColorKey) });
    }
}

public partial class UpdateHouseRequest : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!HouseFlow.Application.Common.HouseColorKeys.IsDefined(ColorKey))
            yield return new ValidationResult("Invalid house colour", new[] { nameof(ColorKey) });
    }
}
