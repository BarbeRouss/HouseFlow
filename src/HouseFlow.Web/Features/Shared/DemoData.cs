using HouseFlow.Web.Api;
using HouseFlow.Web.Rules;

namespace HouseFlow.Web.Features.Shared;

/// <summary>
/// Hard-coded P07 data for the P01 preview and the P02 brand panel (no API call) — the specs/ux
/// mockup data: « Bonjour Marc », 3 maintenances to handle over 2 houses, 8/11 up to date:
/// « Ramonage » (wood stove, Chalet de Spa) overdue by 8 days, « Entretien annuel » (gas boiler,
/// Maison de Namur) due in 12 days, « Test » (smoke detector, Maison de Namur) due in 26 days.
/// Due dates are relative to <paramref name="today"/> so the preview never ages. Names come from
/// the <c>landing.demo.*</c> keys (localized); device types are catalogue values so the tiles get
/// their type tint and icon (DeviceVisuals).
/// </summary>
public static class DemoData
{
    public const int Total = 11;
    public const int UpToDate = 8;
    public const int OverdueDays = 8;
    public const int DueInDays = 12;
    public const int SmokeTestInDays = 26;

    /// <summary>First name of the demo user (« Bonjour Marc », avatar « MR »).</summary>
    public const string UserName = "Marc Renard";

    public static Api.Dashboard Build(DateOnly today, Func<string, string> t)
    {
        var spa = t("landing.demo.houseSpa");
        var namur = t("landing.demo.houseNamur");
        var tasks = new List<UpcomingTask>
        {
            Item(today, "demo-chimney", t("landing.demo.chimney"), "demo-stove", t("landing.demo.stove"), "Poêle à Bois",
                spa, today.AddDays(-OverdueDays)),
            Item(today, "demo-boiler", t("landing.demo.boilerService"), "demo-boiler-device", t("landing.demo.boiler"), "Chaudière Gaz",
                namur, today.AddDays(DueInDays)),
            Item(today, "demo-smoke", t("landing.demo.smokeTest"), "demo-smoke-device", t("landing.demo.smoke"), "Détecteur de Fumée",
                namur, today.AddDays(SmokeTestInDays)),
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

    private static UpcomingTask Item(DateOnly today, string id, string name, string deviceId, string device, string deviceType,
        string house, DateOnly due) => new()
    {
        MaintenanceTypeId = id,
        MaintenanceTypeName = name,
        DeviceId = deviceId,
        DeviceName = device,
        DeviceType = deviceType,
        HouseId = "demo-house",
        HouseName = house,
        Status = StatusRules.Compute(due, today) == DueStatus.Overdue ? "overdue" : "pending",
        NextDueDate = due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        Periodicity = PeriodicityRules.Annual,
        CanLogMaintenance = true,
    };
}
