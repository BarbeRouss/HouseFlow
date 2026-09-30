using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;

namespace HouseFlow.Application.Services;

public class MaintenanceCalculatorService : IMaintenanceCalculatorService
{
    private readonly TimeProvider _timeProvider;

    /// <param name="timeProvider">Clock (tests); the system clock when not provided.</param>
    public MaintenanceCalculatorService(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public DateTime Today => ParisClock.Today(_timeProvider);

    public DateTime CalculateNextDueDate(DateTime lastDate, Periodicity periodicity, int? customDays, int? customMonths = null)
    {
        var date = ParisClock.AsDate(lastDate);
        return periodicity switch
        {
            Periodicity.Annual => date.AddYears(1),
            Periodicity.Biennial => date.AddYears(2),
            Periodicity.Semestrial => date.AddMonths(6),
            Periodicity.Quarterly => date.AddMonths(3),
            Periodicity.Monthly => date.AddMonths(1),
            Periodicity.Custom when customMonths.HasValue => date.AddMonths(customMonths.Value),
            Periodicity.Custom when customDays.HasValue => date.AddDays(customDays.Value),
            Periodicity.Custom => throw new ArgumentException(
                "customMonths or customDays is required when periodicity is Custom.", nameof(customDays)),
            _ => date.AddYears(1)
        };
    }

    public DateTime CalculateNextDueDate(MaintenanceTypeSnapshot snapshot)
    {
        if (snapshot.LastMaintenanceDate is { } last)
            return CalculateNextDueDate(last, snapshot.Periodicity, snapshot.CustomDays, snapshot.CustomMonths);

        return snapshot.BaselineDueDate is { } baseline
            ? ParisClock.AsDate(baseline)
            : NoHistoryBaseline(snapshot.CreatedAt, olderThanKnown: false);
    }

    public DateTime NoHistoryBaseline(DateTime createdAtUtc, bool olderThanKnown)
    {
        var createdOn = ParisClock.DateOf(createdAtUtc);
        return olderThanKnown ? createdOn : createdOn.AddDays(IMaintenanceCalculatorService.UnknownHistoryDelayDays);
    }

    public string CalculateStatus(DateTime nextDueDate, DateTime today)
    {
        var due = nextDueDate.Date;
        var day = today.Date;
        if (due < day) return MaintenanceStatuses.Overdue;
        if (due <= day.AddDays(IMaintenanceCalculatorService.DueSoonWindowDays)) return MaintenanceStatuses.Pending;
        return MaintenanceStatuses.UpToDate;
    }

    public MaintenanceStatusSummary Summarize(IEnumerable<MaintenanceTypeSnapshot> maintenanceTypes)
    {
        var today = Today;
        int total = 0, upToDate = 0, pending = 0, overdue = 0;

        foreach (var snapshot in maintenanceTypes)
        {
            total++;
            switch (CalculateStatus(CalculateNextDueDate(snapshot), today))
            {
                case MaintenanceStatuses.Overdue: overdue++; break;
                case MaintenanceStatuses.Pending: pending++; break;
                default: upToDate++; break;
            }
        }

        var status = total == 0 ? MaintenanceStatuses.None
            : overdue > 0 ? MaintenanceStatuses.Overdue
            : pending > 0 ? MaintenanceStatuses.Pending
            : MaintenanceStatuses.UpToDate;
        var score = total == 0 ? 100 : (int)Math.Round((double)upToDate / total * 100);

        return new MaintenanceStatusSummary(total, upToDate, pending, overdue, status, score);
    }

    public MaintenanceTypeWithStatusDto CalculateMaintenanceTypeWithStatus(MaintenanceTypeSnapshot snapshot)
    {
        var nextDueDate = CalculateNextDueDate(snapshot);

        return new MaintenanceTypeWithStatusDto(
            snapshot.Id,
            snapshot.Name,
            snapshot.Periodicity,
            snapshot.CustomDays,
            snapshot.CustomMonths,
            snapshot.DeviceId,
            snapshot.CreatedAt,
            CalculateStatus(nextDueDate, Today),
            snapshot.LastMaintenanceDate,
            nextDueDate
        );
    }

    public MaintenanceTypeWithStatusDto? MostUrgent(IEnumerable<MaintenanceTypeSnapshot> maintenanceTypes) =>
        maintenanceTypes
            .Select(CalculateMaintenanceTypeWithStatus)
            .OrderBy(t => t.NextDueDate)
            .FirstOrDefault();
}
