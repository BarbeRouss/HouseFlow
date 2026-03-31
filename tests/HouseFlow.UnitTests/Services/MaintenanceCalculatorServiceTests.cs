using FluentAssertions;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Services;

namespace HouseFlow.UnitTests.Services;

public class MaintenanceCalculatorServiceTests
{
    private readonly MaintenanceCalculatorService _sut = new();

    #region Helpers

    private static MaintenanceType CreateMaintenanceType(
        Periodicity periodicity = Periodicity.Annual,
        int? customDays = null,
        params DateTime[] instanceDates)
    {
        var type = new MaintenanceType
        {
            Id = Guid.NewGuid(),
            Name = "Test Maintenance",
            Periodicity = periodicity,
            CustomDays = customDays,
            DeviceId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
        };

        foreach (var date in instanceDates)
        {
            type.MaintenanceInstances.Add(new MaintenanceInstance
            {
                Id = Guid.NewGuid(),
                Date = date,
                MaintenanceTypeId = type.Id,
                CreatedAt = DateTime.UtcNow,
            });
        }

        return type;
    }

    private static Device CreateDevice(params MaintenanceType[] types)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Type = "Chaudière Gaz",
            HouseId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
        };

        foreach (var type in types)
            device.MaintenanceTypes.Add(type);

        return device;
    }

    private static House CreateHouse(params Device[] devices)
    {
        var house = new House
        {
            Id = Guid.NewGuid(),
            Name = "Test House",
            UserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
        };

        foreach (var device in devices)
            house.Devices.Add(device);

        return house;
    }

    #endregion

    #region CalculateNextDueDate

    [Fact]
    public void CalculateNextDueDate_Annual_AddsOneYear()
    {
        var lastDate = new DateTime(2025, 6, 15);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Annual, null);

        result.Should().Be(new DateTime(2026, 6, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Semestrial_AddsSixMonths()
    {
        var lastDate = new DateTime(2025, 6, 15);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Semestrial, null);

        result.Should().Be(new DateTime(2025, 12, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Quarterly_AddsThreeMonths()
    {
        var lastDate = new DateTime(2025, 6, 15);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Quarterly, null);

        result.Should().Be(new DateTime(2025, 9, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Monthly_AddsOneMonth()
    {
        var lastDate = new DateTime(2025, 6, 15);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Monthly, null);

        result.Should().Be(new DateTime(2025, 7, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Custom_AddsSpecifiedDays()
    {
        var lastDate = new DateTime(2025, 1, 1);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, 90);

        result.Should().Be(new DateTime(2025, 4, 1));
    }

    [Fact]
    public void CalculateNextDueDate_Custom_WithoutCustomDays_ThrowsArgumentException()
    {
        var lastDate = new DateTime(2025, 1, 1);

        var act = () => _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, null);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("customDays")
            .WithMessage("*required*Custom*");
    }

    [Fact]
    public void CalculateNextDueDate_Custom_BoundaryMinimum_AddsOneDay()
    {
        var lastDate = new DateTime(2025, 1, 1);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, 1);

        result.Should().Be(new DateTime(2025, 1, 2));
    }

    [Fact]
    public void CalculateNextDueDate_Custom_BoundaryMaximum_Adds3650Days()
    {
        var lastDate = new DateTime(2025, 1, 1);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, 3650);

        result.Should().Be(lastDate.AddDays(3650));
    }

    [Fact]
    public void CalculateNextDueDate_Annual_LeapYearHandling()
    {
        // Feb 29 in a leap year → next year should be Feb 28
        var lastDate = new DateTime(2024, 2, 29);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Annual, null);

        result.Should().Be(new DateTime(2025, 2, 28));
    }

    #endregion

    #region CalculateMaintenanceTypeStatus

    [Fact]
    public void CalculateMaintenanceTypeStatus_NoInstances_ReturnsPending()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        var today = new DateTime(2026, 3, 31);

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_RecentMaintenance_ReturnsUpToDate()
    {
        // Last maintenance 1 month ago, annual periodicity → 11 months left
        var today = new DateTime(2026, 3, 31);
        var type = CreateMaintenanceType(Periodicity.Annual, null, today.AddMonths(-1));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("up_to_date");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueWithin30Days_ReturnsPending()
    {
        // Last maintenance 11.5 months ago → due in ~2 weeks
        var today = new DateTime(2026, 3, 31);
        var type = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2025, 4, 15));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_PastDue_ReturnsOverdue()
    {
        // Last maintenance 2 years ago with annual periodicity → overdue
        var today = new DateTime(2026, 3, 31);
        var type = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2024, 1, 1));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("overdue");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_ExactlyOnDueDate_ReturnsPending()
    {
        // Due date == today → within 30-day window → pending
        var today = new DateTime(2026, 3, 31);
        var type = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2025, 3, 31));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueTomorrow_ReturnsPending()
    {
        var today = new DateTime(2026, 3, 31);
        // Due date is April 1 (tomorrow) → within 30-day window
        var type = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2025, 4, 1));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueYesterday_ReturnsOverdue()
    {
        var today = new DateTime(2026, 3, 31);
        // Due date was March 30 (yesterday) → overdue
        var type = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2025, 3, 30));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("overdue");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_Exactly30DaysAway_ReturnsPending()
    {
        var today = new DateTime(2026, 3, 1);
        // Annual: last done March 31 2025 → due March 31 2026 → exactly 30 days from March 1
        var type = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2025, 3, 31));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_31DaysAway_ReturnsUpToDate()
    {
        var today = new DateTime(2026, 2, 28);
        // Annual: last done March 31 2025 → due March 31 2026 → 31 days from Feb 28
        var type = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2025, 3, 31));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("up_to_date");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_MultipleInstances_UsesLatest()
    {
        var today = new DateTime(2026, 3, 31);
        // Oldest: 2 years ago (would be overdue), newest: 1 month ago (up to date)
        var type = CreateMaintenanceType(Periodicity.Annual, null,
            new DateTime(2024, 1, 1),
            new DateTime(2026, 3, 1));

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("up_to_date");
    }

    #endregion

    #region CalculateDeviceScore

    [Fact]
    public void CalculateDeviceScore_NoMaintenanceTypes_Returns100UpToDate()
    {
        var device = CreateDevice();

        var result = _sut.CalculateDeviceScore(device);

        result.Score.Should().Be(100);
        result.Status.Should().Be("up_to_date");
        result.PendingCount.Should().Be(0);
    }

    [Fact]
    public void CalculateDeviceScore_AllUpToDate_Returns100()
    {
        var today = new DateTime(2026, 3, 31);
        var type1 = CreateMaintenanceType(Periodicity.Annual, null, today.AddMonths(-1));
        var type2 = CreateMaintenanceType(Periodicity.Semestrial, null, today.AddMonths(-1));
        var device = CreateDevice(type1, type2);

        var result = _sut.CalculateDeviceScore(device, today);

        result.Score.Should().Be(100);
        result.Status.Should().Be("up_to_date");
        result.PendingCount.Should().Be(0);
    }

    [Fact]
    public void CalculateDeviceScore_AllOverdue_Returns0()
    {
        var today = new DateTime(2026, 3, 31);
        var type1 = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2024, 1, 1));
        var type2 = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2024, 6, 1));
        var device = CreateDevice(type1, type2);

        var result = _sut.CalculateDeviceScore(device, today);

        result.Score.Should().Be(0);
        result.Status.Should().Be("overdue");
        result.PendingCount.Should().Be(2);
    }

    [Fact]
    public void CalculateDeviceScore_MixedStatuses_CorrectScoreAndStatus()
    {
        var today = new DateTime(2026, 3, 31);
        // 1 up_to_date, 1 overdue, 1 pending (no instances)
        var upToDate = CreateMaintenanceType(Periodicity.Annual, null, today.AddMonths(-1));
        var overdue = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2024, 1, 1));
        var pending = CreateMaintenanceType(Periodicity.Monthly);
        var device = CreateDevice(upToDate, overdue, pending);

        var result = _sut.CalculateDeviceScore(device, today);

        // 1 out of 3 up_to_date = 33%
        result.Score.Should().Be(33);
        result.Status.Should().Be("overdue"); // has at least one overdue
        result.PendingCount.Should().Be(2); // overdue + pending
    }

    [Fact]
    public void CalculateDeviceScore_OnlyPending_StatusIsPending()
    {
        var today = new DateTime(2026, 3, 31);
        var pending = CreateMaintenanceType(Periodicity.Annual); // no instances
        var device = CreateDevice(pending);

        var result = _sut.CalculateDeviceScore(device, today);

        result.Score.Should().Be(0);
        result.Status.Should().Be("pending");
        result.PendingCount.Should().Be(1);
    }

    [Fact]
    public void CalculateDeviceScore_OneOfTwo_Rounds50()
    {
        var today = new DateTime(2026, 3, 31);
        var upToDate = CreateMaintenanceType(Periodicity.Annual, null, today.AddMonths(-1));
        var overdue = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2024, 1, 1));
        var device = CreateDevice(upToDate, overdue);

        var result = _sut.CalculateDeviceScore(device, today);

        result.Score.Should().Be(50);
    }

    [Fact]
    public void CalculateDeviceScore_TwoOfThree_Rounds67()
    {
        var today = new DateTime(2026, 3, 31);
        var type1 = CreateMaintenanceType(Periodicity.Annual, null, today.AddMonths(-1));
        var type2 = CreateMaintenanceType(Periodicity.Semestrial, null, today.AddMonths(-1));
        var overdue = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2024, 1, 1));
        var device = CreateDevice(type1, type2, overdue);

        var result = _sut.CalculateDeviceScore(device, today);

        // 2/3 = 66.67 → rounds to 67
        result.Score.Should().Be(67);
    }

    #endregion

    #region CalculateHouseScore

    [Fact]
    public void CalculateHouseScore_NoDevices_Returns100()
    {
        var house = CreateHouse();

        var result = _sut.CalculateHouseScore(house);

        result.Score.Should().Be(100);
        result.PendingCount.Should().Be(0);
        result.OverdueCount.Should().Be(0);
    }

    [Fact]
    public void CalculateHouseScore_DevicesWithNoMaintenanceTypes_Returns100()
    {
        var device = CreateDevice();
        var house = CreateHouse(device);

        var result = _sut.CalculateHouseScore(house);

        result.Score.Should().Be(100);
    }

    [Fact]
    public void CalculateHouseScore_AllUpToDate_Returns100()
    {
        var today = new DateTime(2026, 3, 31);
        var type1 = CreateMaintenanceType(Periodicity.Annual, null, today.AddMonths(-1));
        var type2 = CreateMaintenanceType(Periodicity.Semestrial, null, today.AddMonths(-1));
        var device1 = CreateDevice(type1);
        var device2 = CreateDevice(type2);
        var house = CreateHouse(device1, device2);

        var result = _sut.CalculateHouseScore(house, today);

        result.Score.Should().Be(100);
        result.PendingCount.Should().Be(0);
        result.OverdueCount.Should().Be(0);
    }

    [Fact]
    public void CalculateHouseScore_MixedAcrossDevices_AggregatesCorrectly()
    {
        var today = new DateTime(2026, 3, 31);
        // Device 1: 1 up_to_date
        var upToDate = CreateMaintenanceType(Periodicity.Annual, null, today.AddMonths(-1));
        var device1 = CreateDevice(upToDate);
        // Device 2: 1 overdue, 1 pending (no instances)
        var overdue = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2024, 1, 1));
        var pending = CreateMaintenanceType(Periodicity.Monthly);
        var device2 = CreateDevice(overdue, pending);
        var house = CreateHouse(device1, device2);

        var result = _sut.CalculateHouseScore(house, today);

        // 1 out of 3 up_to_date = 33%
        result.Score.Should().Be(33);
        result.PendingCount.Should().Be(1);
        result.OverdueCount.Should().Be(1);
    }

    [Fact]
    public void CalculateHouseScore_OverdueNotCountedAsPending()
    {
        // Overdue items should only count in OverdueCount, not PendingCount
        var today = new DateTime(2026, 3, 31);
        var overdue = CreateMaintenanceType(Periodicity.Annual, null, new DateTime(2024, 1, 1));
        var device = CreateDevice(overdue);
        var house = CreateHouse(device);

        var result = _sut.CalculateHouseScore(house, today);

        result.PendingCount.Should().Be(0);
        result.OverdueCount.Should().Be(1);
    }

    #endregion

    #region CalculateMaintenanceTypeWithStatus

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_NoInstances_ReturnsPendingWithNullDates()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        var today = new DateTime(2026, 3, 31);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, today);

        result.Status.Should().Be("pending");
        result.LastMaintenanceDate.Should().BeNull();
        result.NextDueDate.Should().BeNull();
        result.Id.Should().Be(type.Id);
        result.Name.Should().Be(type.Name);
        result.Periodicity.Should().Be(Periodicity.Annual);
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_UpToDate_ReturnsCorrectDates()
    {
        var today = new DateTime(2026, 3, 31);
        var lastDone = today.AddMonths(-1); // March 1
        var type = CreateMaintenanceType(Periodicity.Annual, null, lastDone);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, today);

        result.Status.Should().Be("up_to_date");
        result.LastMaintenanceDate.Should().Be(lastDone);
        result.NextDueDate.Should().Be(lastDone.AddYears(1)); // March 1 2027
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_Overdue_ReturnsCorrectStatus()
    {
        var today = new DateTime(2026, 3, 31);
        var lastDone = new DateTime(2024, 1, 1);
        var type = CreateMaintenanceType(Periodicity.Annual, null, lastDone);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, today);

        result.Status.Should().Be("overdue");
        result.LastMaintenanceDate.Should().Be(lastDone);
        result.NextDueDate.Should().Be(new DateTime(2025, 1, 1));
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_CustomPeriodicity_CalculatesCorrectly()
    {
        var today = new DateTime(2026, 3, 31);
        var lastDone = today.AddDays(-10);
        var type = CreateMaintenanceType(Periodicity.Custom, 45, lastDone);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, today);

        result.Status.Should().Be("up_to_date"); // 35 days left > 30
        result.NextDueDate.Should().Be(lastDone.AddDays(45));
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_PreservesAllFields()
    {
        var type = CreateMaintenanceType(Periodicity.Quarterly, null);
        var today = new DateTime(2026, 3, 31);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, today);

        result.Id.Should().Be(type.Id);
        result.Name.Should().Be(type.Name);
        result.Periodicity.Should().Be(Periodicity.Quarterly);
        result.CustomDays.Should().BeNull();
        result.DeviceId.Should().Be(type.DeviceId);
        result.CreatedAt.Should().Be(type.CreatedAt);
    }

    #endregion
}
