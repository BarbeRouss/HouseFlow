using HouseFlow.Web.Api;
using HouseFlow.Web.Components;
using HouseFlow.Web.Rules;

namespace HouseFlow.Web.Features.Shared;

/// <summary>
/// Hard-coded P07 data for the P01 preview (no API call): 1 house « Ma maison », 8 maintenances,
/// 6 up to date, 2 to handle — « Ramonage » overdue by 8 days and « Entretien annuel » due in
/// 15 days. Due dates are relative to <paramref name="today"/> so the preview never ages. Names
/// come from the <c>landing.demo.*</c> keys (localized).
/// </summary>
public static class DemoData
{
    public const int Total = 8;
    public const int UpToDate = 6;
    public const int OverdueDays = 8;
    public const int DueInDays = 15;

    public static Api.Dashboard Build(DateOnly today, Func<string, string> t)
    {
        var house = t("landing.demo.house");
        var tasks = new List<UpcomingTask>
        {
            Item(today, "demo-chimney", t("landing.demo.chimney"), "demo-stove", t("landing.demo.stove"), house,
                today.AddDays(-OverdueDays)),
            Item(today, "demo-boiler", t("landing.demo.boilerService"), "demo-boiler-device", t("landing.demo.boiler"), house,
                today.AddDays(DueInDays)),
        };
        return new Api.Dashboard
        {
            Tasks = tasks,
            ToHandleCount = tasks.Count,
            OverdueCount = tasks.Count(x => x.Status == "overdue"),
            PendingCount = tasks.Count(x => x.Status == "pending"),
            UpToDateCount = UpToDate,
            TotalCount = Total,
        };
    }

    /// <summary>The C3 rows of the preview (« C'est fait » shown, nothing else).</summary>
    public static List<MaintenanceRowItem> Rows(Api.Dashboard demo) =>
        demo.Tasks.Select(x => MaintenanceRowItem.From(x)).ToList();

    private static UpcomingTask Item(DateOnly today, string id, string name, string deviceId, string device, string house, DateOnly due) => new()
    {
        MaintenanceTypeId = id,
        MaintenanceTypeName = name,
        DeviceId = deviceId,
        DeviceName = device,
        HouseId = "demo-house",
        HouseName = house,
        Status = StatusRules.Compute(due, today) == DueStatus.Overdue ? "overdue" : "pending",
        NextDueDate = due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        Periodicity = PeriodicityRules.Annual,
        CanLogMaintenance = true,
    };
}
