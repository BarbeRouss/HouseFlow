using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.Common;

/// <summary>
/// Validates that a date is not after today's date in Europe/Paris (R1). Maintenance dates are
/// calendar dates sent as <c>yyyy-MM-ddT00:00:00Z</c>: comparing them to <c>DateTime.UtcNow</c>
/// refused a "C'est fait" logged between midnight and 01:00/02:00 in Paris.
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
        if (value is DateTime date) return !IsInFuture(date);
        return false;
    }

    /// <summary>True when the calendar date of <paramref name="date"/> is after today in Europe/Paris.</summary>
    public static bool IsInFuture(DateTime date, TimeProvider? timeProvider = null) =>
        ParisClock.AsDate(date) > ParisClock.Today(timeProvider);
}
