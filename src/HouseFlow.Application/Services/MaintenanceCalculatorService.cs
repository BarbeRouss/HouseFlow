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

    public DateTime CalculateNextDueDate(DateTime lastDate, Periodicity periodicity, int? customDays, int? customMonths = null) =>
        new MaintenanceSchedule(periodicity, customDays, customMonths).NextDue(lastDate);

    public DateTime CalculateNextDueDate(MaintenanceTypeSnapshot snapshot)
    {
        var schedule = snapshot.Schedule;
        if (snapshot.LastMaintenanceDate is { } last)
            return schedule.NextDue(last);

        var baseline = snapshot.BaselineDueDate is { } stored
            ? ParisClock.AsDate(stored)
            : NoHistoryBaseline(snapshot.CreatedAt, olderThanKnown: false);
        return schedule.NoHistoryDue(baseline, ParisClock.DateOf(snapshot.CreatedAt));
    }

    public DateTime NoHistoryBaseline(DateTime createdAtUtc, bool olderThanKnown)
    {
        var createdOn = ParisClock.DateOf(createdAtUtc);
        return olderThanKnown ? createdOn : createdOn.AddDays(IMaintenanceCalculatorService.UnknownHistoryDelayDays);
    }

    public string CalculateStatus(DateTime nextDueDate, DateTime today, Periodicity periodicity, int? customDays, int? customMonths = null) =>
        CalculateStatus(nextDueDate, today, new MaintenanceSchedule(periodicity, customDays, customMonths));

    public string CalculateStatus(DateTime nextDueDate, DateTime today, MaintenanceSchedule schedule)
    {
        var due = nextDueDate.Date;
        var day = today.Date;
        if (due < day) return MaintenanceStatuses.Overdue;
        var windowDays = Math.Max(1, (int)Math.Ceiling(schedule.PeriodDaysAfter(due) * IMaintenanceCalculatorService.DueSoonWindowRatio));
        if (due <= day.AddDays(windowDays)) return MaintenanceStatuses.Pending;
        return MaintenanceStatuses.UpToDate;
    }

    public MaintenanceStatusSummary Summarize(IEnumerable<MaintenanceTypeSnapshot> maintenanceTypes)
    {
        var today = Today;
        int total = 0, upToDate = 0, pending = 0, overdue = 0;

        foreach (var snapshot in maintenanceTypes)
        {
            total++;
            switch (CalculateStatus(CalculateNextDueDate(snapshot), today, snapshot.Schedule))
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
            snapshot.ScheduleMode,
            snapshot.AnchorDay,
            snapshot.AnchorMonth,
            snapshot.DeviceId,
            snapshot.CreatedAt,
            CalculateStatus(nextDueDate, Today, snapshot.Schedule),
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
