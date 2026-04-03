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
        var lastDate = new DateTime(2025, 1, 1);
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Semestrial, null);
        result.Should().Be(new DateTime(2025, 7, 1));
    }

    [Fact]
    public void CalculateNextDueDate_Quarterly_AddsThreeMonths()
    {
        var lastDate = new DateTime(2025, 1, 1);
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Quarterly, null);
        result.Should().Be(new DateTime(2025, 4, 1));
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
        var lastDate = new DateTime(2025, 1, 1);
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, 45);
        result.Should().Be(new DateTime(2025, 2, 15));
    }

    [Fact]
    public void CalculateNextDueDate_Custom_NullDays_DefaultsTo365()
    {
        var lastDate = new DateTime(2025, 1, 1);
        var result = _sut.CalculateNextDueDate(lastDate, Periodicity.Custom, null);
        result.Should().Be(new DateTime(2026, 1, 1));
    }

    #endregion

    #region CalculateMaintenanceTypeStatus

    [Fact]
    public void CalculateMaintenanceTypeStatus_NoInstances_ReturnsPending()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);

        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 6, 1));

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_Overdue_ReturnsOverdue()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 1, 1), // Monthly → due Feb 1
            MaintenanceTypeId = type.Id
        });

        // Today is March 1 → overdue
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 3, 1));

        result.Should().Be("overdue");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueWithin30Days_ReturnsPending()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 1, 1), // Monthly → due Feb 1
            MaintenanceTypeId = type.Id
        });

        // Today is Jan 15 → due in 17 days → pending
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 1, 15));

        result.Should().Be("pending");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_DueMoreThan30DaysAway_ReturnsUpToDate()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 1, 1), // Annual → due Jan 1, 2026
            MaintenanceTypeId = type.Id
        });

        // Today is Feb 1 → due in ~11 months → up_to_date
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 2, 1));

        result.Should().Be("up_to_date");
    }

    [Fact]
    public void CalculateMaintenanceTypeStatus_UsesLatestInstance()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2023, 1, 1), // Old instance
            MaintenanceTypeId = type.Id
        });
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 6, 1), // Recent instance → due June 1, 2026
            MaintenanceTypeId = type.Id
        });

        // Today is July 1, 2025 → due in ~11 months → up_to_date
        var result = _sut.CalculateMaintenanceTypeStatus(type, new DateTime(2025, 7, 1));

        result.Should().Be("up_to_date");
    }

    #endregion

    #region CalculateDeviceScore

    [Fact]
    public void CalculateDeviceScore_NoMaintenanceTypes_Returns100()
    {
        var device = CreateDevice();

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device, new DateTime(2025, 6, 1));

        score.Should().Be(100);
        status.Should().Be("up_to_date");
        pendingCount.Should().Be(0);
    }

    [Fact]
    public void CalculateDeviceScore_AllUpToDate_Returns100()
    {
        var device = CreateDevice();
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 1), // Annual → due May 1, 2026
            MaintenanceTypeId = type.Id
        });
        device.MaintenanceTypes.Add(type);

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device, new DateTime(2025, 6, 1));

        score.Should().Be(100);
        status.Should().Be("up_to_date");
        pendingCount.Should().Be(0);
    }

    [Fact]
    public void CalculateDeviceScore_MixedStatuses_CalculatesCorrectly()
    {
        var device = CreateDevice();
        var today = new DateTime(2025, 6, 1);

        // Type 1: up_to_date (annual, done recently)
        var type1 = CreateMaintenanceType(Periodicity.Annual);
        type1.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 1),
            MaintenanceTypeId = type1.Id
        });

        // Type 2: overdue (monthly, done 3 months ago)
        var type2 = CreateMaintenanceType(Periodicity.Monthly);
        type2.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 3, 1), // Monthly → due April 1 → overdue
            MaintenanceTypeId = type2.Id
        });

        device.MaintenanceTypes.Add(type1);
        device.MaintenanceTypes.Add(type2);

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device, today);

        score.Should().Be(50); // 1 out of 2 up_to_date
        status.Should().Be("overdue");
        pendingCount.Should().Be(1);
    }

    [Fact]
    public void CalculateDeviceScore_AllOverdue_Returns0()
    {
        var device = CreateDevice();
        var today = new DateTime(2025, 6, 1);

        var type1 = CreateMaintenanceType(Periodicity.Monthly);
        type1.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 1, 1),
            MaintenanceTypeId = type1.Id
        });

        var type2 = CreateMaintenanceType(Periodicity.Monthly);
        type2.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 2, 1),
            MaintenanceTypeId = type2.Id
        });

        device.MaintenanceTypes.Add(type1);
        device.MaintenanceTypes.Add(type2);

        var (score, status, pendingCount) = _sut.CalculateDeviceScore(device, today);

        score.Should().Be(0);
        status.Should().Be("overdue");
        pendingCount.Should().Be(2);
    }

    #endregion

    #region CalculateHouseScore

    [Fact]
    public void CalculateHouseScore_NoDevices_Returns100()
    {
        var house = CreateHouse();

        var (score, pendingCount, overdueCount) = _sut.CalculateHouseScore(house, new DateTime(2025, 6, 1));

        score.Should().Be(100);
        pendingCount.Should().Be(0);
        overdueCount.Should().Be(0);
    }

    [Fact]
    public void CalculateHouseScore_MultipleDevices_AggregatesCorrectly()
    {
        var house = CreateHouse();
        var today = new DateTime(2025, 6, 1);

        // Device 1 with up_to_date type
        var device1 = CreateDevice();
        var type1 = CreateMaintenanceType(Periodicity.Annual);
        type1.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 1),
            MaintenanceTypeId = type1.Id
        });
        device1.MaintenanceTypes.Add(type1);

        // Device 2 with overdue type
        var device2 = CreateDevice();
        var type2 = CreateMaintenanceType(Periodicity.Monthly);
        type2.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 1, 1),
            MaintenanceTypeId = type2.Id
        });
        device2.MaintenanceTypes.Add(type2);

        // Device 2 with pending type (due within 30 days)
        var type3 = CreateMaintenanceType(Periodicity.Monthly);
        type3.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 15), // Monthly → due June 15 → 14 days away → pending
            MaintenanceTypeId = type3.Id
        });
        device2.MaintenanceTypes.Add(type3);

        house.Devices.Add(device1);
        house.Devices.Add(device2);

        var (score, pendingCount, overdueCount) = _sut.CalculateHouseScore(house, today);

        score.Should().Be(33); // 1 out of 3 up_to_date → 33%
        pendingCount.Should().Be(1);
        overdueCount.Should().Be(1);
    }

    #endregion

    #region CalculateMaintenanceTypeWithStatus

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_NoInstances_ReturnsPendingWithNullDates()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, new DateTime(2025, 6, 1));

        result.Status.Should().Be("pending");
        result.LastMaintenanceDate.Should().BeNull();
        result.NextDueDate.Should().BeNull();
        result.Id.Should().Be(type.Id);
        result.Name.Should().Be(type.Name);
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_WithInstance_ReturnsCorrectDatesAndStatus()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);
        var instanceDate = new DateTime(2025, 5, 1);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = instanceDate,
            MaintenanceTypeId = type.Id
        });

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, new DateTime(2025, 5, 15));

        result.Status.Should().Be("pending"); // Due June 1, within 30 days
        result.LastMaintenanceDate.Should().Be(instanceDate);
        result.NextDueDate.Should().Be(new DateTime(2025, 6, 1));
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_Overdue_ReturnsOverdueStatus()
    {
        var type = CreateMaintenanceType(Periodicity.Monthly);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 1, 1),
            MaintenanceTypeId = type.Id
        });

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, new DateTime(2025, 6, 1));

        result.Status.Should().Be("overdue");
        result.NextDueDate.Should().Be(new DateTime(2025, 2, 1));
    }

    [Fact]
    public void CalculateMaintenanceTypeWithStatus_UpToDate_ReturnsUpToDateStatus()
    {
        var type = CreateMaintenanceType(Periodicity.Annual);
        type.MaintenanceInstances.Add(new MaintenanceInstance
        {
            Id = Guid.NewGuid(),
            Date = new DateTime(2025, 5, 1),
            MaintenanceTypeId = type.Id
        });

        var result = _sut.CalculateMaintenanceTypeWithStatus(type, new DateTime(2025, 6, 1));

        result.Status.Should().Be("up_to_date");
        result.NextDueDate.Should().Be(new DateTime(2026, 5, 1));
    }

    #endregion

    #region Helpers

    private static MaintenanceType CreateMaintenanceType(Periodicity periodicity, int? customDays = null)
    {
        return new MaintenanceType
        {
            Id = Guid.NewGuid(),
            Name = $"Test-{periodicity}",
            Periodicity = periodicity,
            CustomDays = customDays,
            DeviceId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            MaintenanceInstances = new List<MaintenanceInstance>()
        };
    }

    private static Device CreateDevice()
    {
        return new Device
        {
            Id = Guid.NewGuid(),
            Name = "Test Device",
            Type = "Appliance",
            HouseId = Guid.NewGuid(),
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
            Devices = new List<Device>()
        };
    }

    #endregion
}
