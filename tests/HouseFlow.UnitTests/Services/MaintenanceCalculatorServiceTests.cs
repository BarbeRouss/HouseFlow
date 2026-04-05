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
        var lastDate = new DateTime(2025, 12, 31);
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Monthly, null);
        result.Should().Be(new DateTime(2026, 1, 31));
    }

    [Fact]
    public void CalculateNextDueDate_Custom_AddsSpecifiedDays()
    {
        var lastDate = new DateTime(2025, 1, 1);
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, 90);
        result.Should().Be(new DateTime(2025, 4, 1));
    }

    [Fact]
    public void CalculateNextDueDate_Custom_WithNullDays_DefaultsTo365()
    {
        var lastDate = new DateTime(2025, 1, 1);
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, null);
        result.Should().Be(new DateTime(2026, 1, 1));
    }

    [Fact]
    public void CalculateNextDueDate_UnknownPeriodicity_DefaultsToOneYear()
    {
        var lastDate = new DateTime(2025, 6, 1);
        var result = _sut.CalculateNextDueDate(lastDate, (Periodicity)999, null);
        result.Should().Be(new DateTime(2026, 6, 1));
    }

    [Fact]
    public void CalculateNextDueDate_LeapYear_Feb29_Annual()
    {
        var lastDate = new DateTime(2024, 2, 29); // leap year
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Annual, null);
        // .NET AddYears(1) from Feb 29 returns Feb 28 in non-leap year
        result.Should().Be(new DateTime(2025, 2, 28));
    }

    [Fact]
    public void CalculateNextDueDate_EndOfMonth_Monthly()
    {
        var lastDate = new DateTime(2025, 1, 31);
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Monthly, null);
        // Jan 31 + 1 month = Feb 28 (2025 is not a leap year)
        result.Should().Be(new DateTime(2025, 2, 28));
    }

    #endregion

    #region CalculateMaintenanceTypeStatus

    [Fact]
    public void CalculateMaintenanceTypeStatus_NoInstances_ReturnsPending()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);

        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 1));

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_Overdue_ReturnsOverdue()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2023, 1, 1) // over 1 year ago
        });

        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 1));

        result.Should().Be("overdue");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueWithin30Days_ReturnsPending()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        // Last done ~11 months ago, due in ~1 month
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2024, 7, 10)
        });

        // today = 2025-06-20 → next due = 2025-07-10 → 20 days away → pending
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 20));

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueInMoreThan30Days_ReturnsUpToDate()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 1)
        });

        // today = 2025-06-01 → next due = 2026-05-01 → ~11 months away → up_to_date
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 1));

        result.Should().Be("up_to_date");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueExactlyToday_ReturnsOverdue()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2024, 6, 1)
        });

        // today = 2025-06-01 → next due = 2025-06-01 → nextDueDate < today is false (equal)
        // nextDueDate <= today.AddDays(30) is true → pending
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 1));

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueYesterday_ReturnsOverdue()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2024, 5, 31)
        });

        // today = 2025-06-01 → next due = 2025-05-31 → nextDueDate < today → overdue
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 1));

        result.Should().Be("overdue");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_MultipleInstances_UsesLatest()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2020, 1, 1) // old
        });
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 1) // recent
        });

        // Uses 2025-05-01 → next due = 2026-05-01 → up_to_date
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 1));

        result.Should().Be("up_to_date");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_Exactly30DaysOut_ReturnsPending()
    {
        var type = CreateMaintenanceType(Periodicity.Custom, 60);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 2) // + 60 days = 2025-07-01
        });

        // today = 2025-06-01 → next due = 2025-07-01 → exactly 30 days → pending (<=30)
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 1));

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_31DaysOut_ReturnsUpToDate()
    {
        var type = CreateMaintenanceType(Periodicity.Custom, 61);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 1) // + 61 days = 2025-07-01
        });

        // today = 2025-05-31 → next due = 2025-07-01 → 31 days away → up_to_date
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 5, 31));

        result.Should().Be("up_to_date");
    }

    #endregion

    #region CalculateDeviceScore

    [Fact]
    public void CalculateDeviceScore_NoMaintenanceTypes_Returns100()
    {
        var device = CreateDevice();

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device);

        score.Should().Be(100);
        status.Should().Be("up_to_date");
        pendingCount.Should().Be(0);
    }

    [Fact]
    public void CalculateDeviceScore_AllUpToDate_Returns100()
    {
        var device = CreateDevice();
        var recentDate = DateTime.UtcNow.Date.AddDays(-10);

        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device);

        score.Should().Be(100);
        status.Should().Be("up_to_date");
        pendingCount.Should().Be(0);
    }

    [Fact]
    public void CalculateDeviceScore_AllOverdue_Returns0()
    {
        var device = CreateDevice();
        var oldDate = DateTime.UtcNow.Date.AddYears(-3);

        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { oldDate }));
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { oldDate }));

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device);

        score.Should().Be(0);
        status.Should().Be("overdue");
        pendingCount.Should().Be(2);
    }

    [Fact]
    public void CalculateDeviceScore_MixedStatuses_CalculatesCorrectly()
    {
        var device = CreateDevice();
        var recentDate = DateTime.UtcNow.Date.AddDays(-10);
        var oldDate = DateTime.UtcNow.Date.AddYears(-3);

        // 1 up_to_date, 1 overdue → 50%
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { oldDate }));

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device);

        score.Should().Be(50);
        status.Should().Be("overdue");
        pendingCount.Should().Be(1);
    }

    [Fact]
    public void CalculateDeviceScore_OneOfThreeUpToDate_Returns33()
    {
        var device = CreateDevice();
        var recentDate = DateTime.UtcNow.Date.AddDays(-10);
        var oldDate = DateTime.UtcNow.Date.AddYears(-3);

        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { oldDate }));
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { oldDate }));

        var (score, _, _) = _sut.CalculateDeviceScore(device);

        // 1/3 = 33.33... → rounds to 33
        score.Should().Be(33);
    }

    [Fact]
    public void CalculateDeviceScore_PendingButNotOverdue_StatusIsPending()
    {
        var device = CreateDevice();
        // No instances → pending (not overdue)
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual));

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device);

        score.Should().Be(0);
        status.Should().Be("pending");
        pendingCount.Should().Be(1);
    }

    [Fact]
    public void CalculateDeviceScore_TwoOfThreeUpToDate_Returns67()
    {
        var device = CreateDevice();
        var recentDate = DateTime.UtcNow.Date.AddDays(-10);
        var oldDate = DateTime.UtcNow.Date.AddYears(-3);

        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { oldDate }));

        var (score, _, _) = _sut.CalculateDeviceScore(device);

        // 2/3 = 66.66... → rounds to 67
        score.Should().Be(67);
    }

    #endregion

    #region CalculateHouseScore

    [Fact]
    public void CalculateHouseScore_NoDevices_Returns100()
    {
        var house = CreateHouse();

        var (score, pendingCount, overdueCount) = _sut.CalculateHouseScore(house);

        score.Should().Be(100);
        pendingCount.Should().Be(0);
        overdueCount.Should().Be(0);
    }

    [Fact]
    public void CalculateHouseScore_DevicesWithNoMaintenanceTypes_Returns100()
    {
        var house = CreateHouse();
        house.Devices.Add(CreateDevice());
        house.Devices.Add(CreateDevice());

        var (score, pendingCount, overdueCount) = _sut.CalculateHouseScore(house);

        score.Should().Be(100);
        pendingCount.Should().Be(0);
        overdueCount.Should().Be(0);
    }

    [Fact]
    public void CalculateHouseScore_AllUpToDate_Returns100()
    {
        var house = CreateHouse();
        var recentDate = DateTime.UtcNow.Date.AddDays(-10);

        var device1 = CreateDevice();
        device1.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));
        var device2 = CreateDevice();
        device2.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));

        house.Devices.Add(device1);
        house.Devices.Add(device2);

        var (score, pendingCount, overdueCount) = _sut.CalculateHouseScore(house);

        score.Should().Be(100);
        pendingCount.Should().Be(0);
        overdueCount.Should().Be(0);
    }

    [Fact]
    public void CalculateHouseScore_MixedAcrossDevices_CalculatesCorrectly()
    {
        var house = CreateHouse();
        var recentDate = DateTime.UtcNow.Date.AddDays(-10);
        var oldDate = DateTime.UtcNow.Date.AddYears(-3);

        var device1 = CreateDevice();
        device1.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { recentDate }));

        var device2 = CreateDevice();
        device2.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { oldDate }));

        house.Devices.Add(device1);
        house.Devices.Add(device2);

        // 1 up_to_date, 1 overdue → score 50
        var (score, pendingCount, overdueCount) = _sut.CalculateHouseScore(house);

        score.Should().Be(50);
        pendingCount.Should().Be(0);
        overdueCount.Should().Be(1);
    }

    [Fact]
    public void CalculateHouseScore_PendingAndOverdue_CountsSeparately()
    {
        var house = CreateHouse();
        var oldDate = DateTime.UtcNow.Date.AddYears(-3);

        var device = CreateDevice();
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual, instances: new[] { oldDate })); // overdue
        device.MaintenanceTypes.Add(CreateMaintenanceType(Periodicity.Annual)); // pending (no instances)

        house.Devices.Add(device);

        var (score, pendingCount, overdueCount) = _sut.CalculateHouseScore(house);

        score.Should().Be(0);
        pendingCount.Should().Be(1);
        overdueCount.Should().Be(1);
    }

    #endregion

    #region CalculateMaintenanceTypeWithStatus

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_NoInstances_ReturnsPendingWithNullDates()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.Status.Should().Be("pending");
        result.LastMaintenanceDate.Should().BeNull();
        result.NextDueDate.Should().BeNull();
        result.Id.Should().Be(type.Id);
        result.Name.Should().Be(type.Name);
        result.Periodicity.Should().Be(Periodicity.Annual);
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_WithInstance_ReturnsCorrectDates()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        var lastDate = DateTime.UtcNow.Date.AddDays(-10);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = lastDate
        });

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.Status.Should().Be("up_to_date");
        result.LastMaintenanceDate.Should().Be(lastDate);
        result.NextDueDate.Should().Be(lastDate.AddYears(1));
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_Overdue_ReturnsOverdueStatus()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);
        var oldDate = DateTime.UtcNow.Date.AddMonths(-3);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = oldDate
        });

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.Status.Should().Be("overdue");
        result.LastMaintenanceDate.Should().Be(oldDate);
        result.NextDueDate.Should().Be(oldDate.AddMonths(1));
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_CustomPeriodicity_UsesCustomDays()
    {
        var type = CreateMaintenanceType(Periodicity.Custom, customDays: 45);
        var lastDate = DateTime.UtcNow.Date.AddDays(-10);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = lastDate
        });

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.NextDueDate.Should().Be(lastDate.AddDays(45));
        result.CustomDays.Should().Be(45);
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_PreservesAllDtoFields()
    {
        var deviceId = Guid.NewGuid();
        var createdAt = new DateTime(2025, 1, 15);
        var type = new MaintenanceType
        {
            Id = Guid.NewGuid(),
            Name = "Ramonage cheminée",
            Periodicity = Periodicity.Semestrial,
            CustomDays = null,
            DeviceId = deviceId,
            CreatedAt = createdAt,
            MaintenanceInstances = new List<MaintenanceInstance>()
        };

        var result = _sut.CalculateMaintenanceTypeWithStatus(type);

        result.Id.Should().Be(type.Id);
        result.Name.Should().Be("Ramonage cheminée");
        result.Periodicity.Should().Be(Periodicity.Semestrial);
        result.CustomDays.Should().BeNull();
        result.DeviceId.Should().Be(deviceId);
        result.CreatedAt.Should().Be(createdAt);
    }

    #endregion

    #region Helpers

    private static MaintenanceType CreateMaintenanceType(
        Periodicity periodicity,
        int? customDays = null,
        DateTime[]? instances = null)
    {
        var type = new MaintenanceType
        {
            Id = Guid.NewGuid(),
            Name = "Test Maintenance",
            Periodicity = periodicity,
            CustomDays = customDays,
            DeviceId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            MaintenanceInstances = new List<MaintenanceInstance>()
        };

        if (instances != null)
        {
            foreach (var date in instances)
            {
                type.MaintenanceInstances.Add(new MaintenanceInstance
                {
                    Id = Guid.NewGuid(),
                    Date = date
                });
            }
        }

        return type;
    }

    private static Device CreateDevice()
    {
        return new Device
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Type = "Chaudière Gaz",
            HouseId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            MaintenanceTypes = new List<MaintenanceType>()
        };
    }

    private static House CreateHouse()
    {
        return new House
        {
            Id = Guid.NewGuid(),
            Name = "Test House",
            UserId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            Devices = new List<Device>()
        };
    }

    #endregion
}
