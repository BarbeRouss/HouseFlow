using FluentAssertions;
using HouseFlow.Core.Entities;
using HouseFlow.Infrastructure.Services;

namespace HouseFlow.UnitTests.Services;

public class MaintenanceCalculatorServiceTests
{
    private readonly MaintenanceCalculatorService _sut = new();

    #region CalculateNextDueDate

    [Fact]
    public void CalculateNextDueDate_Annual_AddsOneYear()
    {
        var lastDate = new DateTime(2025, 3, 15);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Annual, null);

        result.Should().Be(new DateTime(2026, 3, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Semestrial_AddsSixMonths()
    {
        var lastDate = new DateTime(2025, 1, 10);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Semestrial, null);

        result.Should().Be(new DateTime(2025, 7, 10));
    }

    [Fact]
    public void CalculateNextDueDate_Quarterly_AddsThreeMonths()
    {
        var lastDate = new DateTime(2025, 10, 1);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Quarterly, null);

        result.Should().Be(new DateTime(2026, 1, 1));
    }

    [Fact]
    public void CalculateNextDueDate_Monthly_AddsOneMonth()
    {
        var lastDate = new DateTime(2025, 1, 31);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Monthly, null);

        result.Should().Be(new DateTime(2025, 2, 28));
    }

    [Fact]
    public void CalculateNextDueDate_Custom_AddsSpecifiedDays()
    {
        var lastDate = new DateTime(2025, 6, 1);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, 45);

        result.Should().Be(new DateTime(2025, 7, 16));
    }

    [Fact]
    public void CalculateNextDueDate_Custom_WithNullDays_DefaultsTo365()
    {
        var lastDate = new DateTime(2025, 1, 1);

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, null);

        result.Should().Be(new DateTime(2026, 1, 1));
    }

    [Fact]
    public void CalculateNextDueDate_LeapYear_Feb29_AnnualGoesToFeb28()
    {
        var lastDate = new DateTime(2024, 2, 29); // leap year

        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Annual, null);

        result.Should().Be(new DateTime(2025, 2, 28)); // non-leap year
    }

    [Fact]
    public void CalculateNextDueDate_UnknownPeriodicity_DefaultsToOneYear()
    {
        var lastDate = new DateTime(2025, 5, 1);

        var result = _sut.CalculateNextDueDate(lastDate, (Periodicity)999, null);

        result.Should().Be(new DateTime(2026, 5, 1));
    }

    #endregion

    #region CalculateMaintenanceTypeStatus

    [Fact]
    public void CalculateMaintenanceTypeStatus_NoInstances_ReturnsPending()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);

        var result = _sut.CalculateMaintenanceTypeStatus(type, DateTime.UtcNow.Date);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_NextDueDatePassed_ReturnsOverdue()
    {
        var today = new DateTime(2026, 4, 5);
        var type = CreateMaintenanceType(Periodicity.Monthly,
            new MaintenanceInstance { Date = new DateTime(2026, 2, 1) }); // due March 1 → overdue

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("overdue");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_NextDueDateWithin30Days_ReturnsPending()
    {
        var today = new DateTime(2026, 4, 5);
        var type = CreateMaintenanceType(Periodicity.Monthly,
            new MaintenanceInstance { Date = new DateTime(2026, 3, 20) }); // due April 20 → within 30 days

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_NextDueDateExactlyToday_ReturnsPending()
    {
        var today = new DateTime(2026, 4, 5);
        var type = CreateMaintenanceType(Periodicity.Monthly,
            new MaintenanceInstance { Date = new DateTime(2026, 3, 5) }); // due April 5 = today

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_NextDueDateExactly30DaysAway_ReturnsPending()
    {
        var today = new DateTime(2026, 4, 5);
        var type = CreateMaintenanceType(Periodicity.Custom, customDays: 60,
            new MaintenanceInstance { Date = new DateTime(2026, 3, 6) }); // due May 5 = today+30

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_NextDueDateBeyond30Days_ReturnsUpToDate()
    {
        var today = new DateTime(2026, 4, 5);
        var type = CreateMaintenanceType(Periodicity.Annual,
            new MaintenanceInstance { Date = new DateTime(2026, 4, 1) }); // due April 1, 2027

        var result = _sut.CalculateMaintenanceTypeStatus(type, today);

        result.Should().Be("up_to_date");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_MultipleInstances_UsesLatest()
    {
        var today = new DateTime(2026, 4, 5);
        var type = CreateMaintenanceType(Periodicity.Annual,
            new MaintenanceInstance { Date = new DateTime(2024, 1, 1) },  // old — would be overdue
            new MaintenanceInstance { Date = new DateTime(2026, 4, 1) }); // recent — up_to_date

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
        var recentDate = DateTime.UtcNow.Date.AddDays(-1);
        var device = CreateDevice(
            CreateMaintenanceType(Periodicity.Annual, new MaintenanceInstance { Date = recentDate }),
            CreateMaintenanceType(Periodicity.Annual, new MaintenanceInstance { Date = recentDate })
        );

        var result = _sut.CalculateDeviceScore(device);

        result.Score.Should().Be(100);
        result.Status.Should().Be("up_to_date");
        result.PendingCount.Should().Be(0);
    }

    [Fact]
    public void CalculateDeviceScore_OneOverdue_StatusIsOverdue()
    {
        var device = CreateDevice(
            CreateMaintenanceType(Periodicity.Annual, new MaintenanceInstance { Date = DateTime.UtcNow.Date.AddDays(-1) }),
            CreateMaintenanceType(Periodicity.Monthly, new MaintenanceInstance { Date = DateTime.UtcNow.Date.AddMonths(-2) }) // overdue
        );

        var result = _sut.CalculateDeviceScore(device);

        result.Status.Should().Be("overdue");
        result.Score.Should().Be(50); // 1 out of 2 up_to_date
        result.PendingCount.Should().Be(1);
    }

    [Fact]
    public void CalculateDeviceScore_AllPending_NoInstances_Returns0Pending()
    {
        var device = CreateDevice(
            CreateMaintenanceType(Periodicity.Monthly),
            CreateMaintenanceType(Periodicity.Annual)
        );

        var result = _sut.CalculateDeviceScore(device);

        result.Score.Should().Be(0);
        result.Status.Should().Be("pending");
        result.PendingCount.Should().Be(2);
    }

    [Fact]
    public void CalculateDeviceScore_MixedStatuses_CalculatesCorrectScore()
    {
        var recentDate = DateTime.UtcNow.Date.AddDays(-1);
        var device = CreateDevice(
            CreateMaintenanceType(Periodicity.Annual, new MaintenanceInstance { Date = recentDate }),  // up_to_date
            CreateMaintenanceType(Periodicity.Annual, new MaintenanceInstance { Date = recentDate }),  // up_to_date
            CreateMaintenanceType(Periodicity.Monthly) // pending (no instances)
        );

        var result = _sut.CalculateDeviceScore(device);

        result.Score.Should().Be(67); // Math.Round(2/3 * 100) = 67
        result.Status.Should().Be("pending");
        result.PendingCount.Should().Be(1);
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
        var house = CreateHouse(CreateDevice());

        var result = _sut.CalculateHouseScore(house);

        result.Score.Should().Be(100);
    }

    [Fact]
    public void CalculateHouseScore_AggregatesAcrossDevices()
    {
        var recentDate = DateTime.UtcNow.Date.AddDays(-1);
        var house = CreateHouse(
            CreateDevice(
                CreateMaintenanceType(Periodicity.Annual, new MaintenanceInstance { Date = recentDate }) // up_to_date
            ),
            CreateDevice(
                CreateMaintenanceType(Periodicity.Monthly, new MaintenanceInstance { Date = DateTime.UtcNow.Date.AddMonths(-2) }) // overdue
            )
        );

        var result = _sut.CalculateHouseScore(house);

        result.Score.Should().Be(50); // 1/2 up_to_date
        result.OverdueCount.Should().Be(1);
        result.PendingCount.Should().Be(0);
    }

    [Fact]
    public void CalculateHouseScore_CountsPendingAndOverdueSeparately()
    {
        var recentDate = DateTime.UtcNow.Date.AddDays(-1);
        var house = CreateHouse(
            CreateDevice(
                CreateMaintenanceType(Periodicity.Annual, new MaintenanceInstance { Date = recentDate }), // up_to_date
                CreateMaintenanceType(Periodicity.Monthly)  // pending (no instances)
            ),
            CreateDevice(
                CreateMaintenanceType(Periodicity.Monthly, new MaintenanceInstance { Date = DateTime.UtcNow.Date.AddMonths(-2) }) // overdue
            )
        );

        var result = _sut.CalculateHouseScore(house);

        result.Score.Should().Be(33); // 1/3 up_to_date
        result.PendingCount.Should().Be(1);
        result.OverdueCount.Should().Be(1);
    }

    #endregion

    #region CalculateMaintenanceTypeWithStatus

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_NoInstances_ReturnsPendingWithNullDates()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);
        type.Id = Guid.NewGuid();
        type.Name = "Oil Change";
        type.DeviceId = Guid.NewGuid();
        type.CreatedAt = new DateTime(2025, 1, 1);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.Status.Should().Be("pending");
        result.LastMaintenanceDate.Should().BeNull();
        result.NextDueDate.Should().BeNull();
        result.Name.Should().Be("Oil Change");
        result.Periodicity.Should().Be(Periodicity.Monthly);
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_WithInstance_ReturnsCorrectDates()
    {
        var instanceDate = DateTime.UtcNow.Date.AddDays(-10);
        var type = CreateMaintenanceType(Periodicity.Monthly,
            new MaintenanceInstance { Date = instanceDate });
        type.Id = Guid.NewGuid();
        type.Name = "Filter";
        type.DeviceId = Guid.NewGuid();
        type.CreatedAt = new DateTime(2025, 1, 1);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.LastMaintenanceDate.Should().Be(instanceDate);
        result.NextDueDate.Should().Be(instanceDate.AddMonths(1));
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_OverdueInstance_ReturnsOverdueStatus()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly,
            new MaintenanceInstance { Date = DateTime.UtcNow.Date.AddMonths(-2) });
        type.Id = Guid.NewGuid();
        type.Name = "Test";
        type.DeviceId = Guid.NewGuid();
        type.CreatedAt = new DateTime(2025, 1, 1);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.Status.Should().Be("overdue");
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_RecentInstance_ReturnsUpToDate()
    {
        var type = CreateMaintenanceType(Periodicity.Annual,
            new MaintenanceInstance { Date = DateTime.UtcNow.Date.AddDays(-1) });
        type.Id = Guid.NewGuid();
        type.Name = "Test";
        type.DeviceId = Guid.NewGuid();
        type.CreatedAt = new DateTime(2025, 1, 1);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.Status.Should().Be("up_to_date");
        result.NextDueDate.Should().Be(DateTime.UtcNow.Date.AddDays(-1).AddYears(1));
    }

    #endregion

    #region Helpers

    private static MaintenanceType CreateMaintenanceType(
        Periodicity periodicity,
        params MaintenanceInstance[] instances)
    {
        return CreateMaintenanceType(periodicity, customDays: null, instances);
    }

    private static MaintenanceType CreateMaintenanceType(
        Periodicity periodicity,
        int? customDays,
        params MaintenanceInstance[] instances)
    {
        return new MaintenanceType
        {
            Id = Guid.NewGuid(),
            Name = "Test Type",
            Periodicity = periodicity,
            CustomDays = customDays,
            DeviceId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            MaintenanceInstances = instances.ToList()
        };
    }

    private static Device CreateDevice(params MaintenanceType[] types)
    {
        return new Device
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Type = "Appliance",
            HouseId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            MaintenanceTypes = types.ToList()
        };
    }

    private static House CreateHouse(params Device[] devices)
    {
        return new House
        {
            Id = Guid.NewGuid(),
            Name = "Test House",
            UserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Devices = devices.ToList()
        };
    }

    #endregion
}
