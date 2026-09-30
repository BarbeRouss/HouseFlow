using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;

namespace HouseFlow.Application.Services;

/// <summary>
/// Builds a new maintenance type and applies the R2 « Dernier entretien » choice. Shared by
/// POST /devices/{id}/maintenance-types and POST /houses/{id}/devices (catalogue default type).
/// </summary>
internal static class MaintenanceTypeFactory
{
    /// <summary>Note of the record created from a month + year choice (R2, spec wording, stored as is).</summary>
    public const string ApproximateDateNote = "Date approximative (mois)";

    public static (MaintenanceType Type, MaintenanceInstance? Record) Create(
        Guid deviceId, CreateMaintenanceTypeRequestDto request, IMaintenanceCalculatorService calculator)
    {
        var (customDays, customMonths) = NormalizeCustomInterval(request.Periodicity, request.CustomDays, request.CustomMonths);

        var now = DateTime.UtcNow;
        var type = new MaintenanceType
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Periodicity = request.Periodicity,
            CustomDays = customDays,
            CustomMonths = customMonths,
            DeviceId = deviceId,
            CreatedAt = now
        };

        var choice = request.LastMaintenance;
        switch (choice?.Kind ?? LastMaintenanceKind.Unknown)
        {
            case LastMaintenanceKind.Month:
                var date = FirstOfMonth(choice!, calculator.Today);
                return (type, new MaintenanceInstance
                {
                    Id = Guid.NewGuid(),
                    Date = date,
                    Notes = ApproximateDateNote,
                    MaintenanceTypeId = type.Id,
                    CreatedAt = now
                });

            case LastMaintenanceKind.Older:
                type.BaselineDueDate = calculator.NoHistoryBaseline(now, olderThanKnown: true);
                return (type, null);

            default:
                type.BaselineDueDate = calculator.NoHistoryBaseline(now, olderThanKnown: false);
                return (type, null);
        }
    }

    /// <summary>Maps the device-creation body (generated contract) onto the maintenance-type request.</summary>
    public static CreateMaintenanceTypeRequestDto FromDeviceRequest(DeviceMaintenanceTypeRequestDto request) => new(
        request.Name,
        Enum.Parse<Periodicity>(request.Periodicity.ToString()),
        request.CustomDays,
        request.CustomMonths,
        request.LastMaintenance);

    /// <summary>
    /// Custom periodicity needs exactly one interval (months preferred); any other periodicity carries none.
    /// </summary>
    public static (int? CustomDays, int? CustomMonths) NormalizeCustomInterval(Periodicity periodicity, int? customDays, int? customMonths)
    {
        if (periodicity != Periodicity.Custom) return (null, null);
        if (customMonths is { } months) return (null, months);
        if (customDays is { } days) return (days, null);
        throw new InvalidOperationException("CustomMonths (or CustomDays) is required when periodicity is Custom.");
    }

    private static DateTime FirstOfMonth(LastMaintenanceDto choice, DateTime today)
    {
        if (choice.Year is not { } year || choice.Month is not { } month || month is < 1 or > 12 || year < 1900)
            throw new InvalidOperationException("Last maintenance: year and month (1-12) are required.");

        var date = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        if (date > today)
            throw new InvalidOperationException("Last maintenance cannot be in the future.");
        return date;
    }
}
