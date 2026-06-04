using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.Common;

/// <summary>
/// Validates that a DateTime value is not in the future.
/// Used for maintenance dates which represent work already done.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class NotInFutureAttribute : ValidationAttribute
{
    public NotInFutureAttribute()
        : base("Date cannot be in the future")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null) return true; // Nullable fields: let [Required] handle null
        if (value is DateTime date) return date <= DateTime.UtcNow;
        return false;
    }
}
