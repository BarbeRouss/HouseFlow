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
