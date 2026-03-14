# Plan: List Upcoming Tasks

**Feature**: Add an aggregated view of upcoming maintenance tasks on the dashboard, showing all pending and overdue maintenance across all houses and devices, sorted by urgency.

## Overview

Currently, users must navigate into each house → each device to see what maintenance is due. This feature adds:
1. A new backend endpoint: `GET /api/v1/upcoming-tasks` that aggregates all pending/overdue maintenance types across all user's houses/devices
2. A new section on the dashboard page showing the upcoming tasks list

## Tasks

### Backend

1. **Create UpcomingTaskDto** in `Application/DTOs/MaintenanceDtos.cs`
   - Fields: maintenanceTypeId, maintenanceTypeName, deviceId, deviceName, deviceType, houseId, houseName, status (pending/overdue), nextDueDate, lastMaintenanceDate, periodicity

2. **Create UpcomingTasksResponseDto**
   - Fields: tasks (array of UpcomingTaskDto), overdueCount, pendingCount

3. **Add GetUpcomingTasksAsync method** to `IMaintenanceService` interface and `MaintenanceService` implementation
   - Query all maintenance types for all devices in all user's houses
   - Calculate status for each using MaintenanceCalculatorService
   - Filter to only pending + overdue items
   - Sort: overdue first (by nextDueDate ASC), then pending (by nextDueDate ASC)

4. **Add API endpoint** `GET /api/v1/upcoming-tasks` in a new or existing controller
   - Returns UpcomingTasksResponseDto
   - Requires authentication

5. **Update OpenAPI spec** with the new endpoint

### Frontend

6. **Add API hook** `useUpcomingTasks()` in hooks
7. **Add i18n translations** for upcoming tasks section (fr.json + en.json)
8. **Add UpcomingTasks component** on the dashboard page
   - Show list of upcoming tasks grouped or sorted by urgency
   - Each item: device name, house name, maintenance type, due date, status badge
   - Click navigates to the device detail page
   - Empty state if no upcoming tasks

### Testing

9. **Add backend integration tests** for the new endpoint
10. **Verify E2E** that the dashboard displays upcoming tasks
