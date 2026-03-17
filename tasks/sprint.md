# Sprint: Prochaines tâches

**Objectif:** Afficher les 5 prochaines tâches de maintenance sur le dashboard
**User Story:** US-045
**Créé:** 2026-03-12
**Status:** Terminé

---

## Tâches

### Backend

- [x] Créer DTO `UpcomingTaskDto` (typeId, typeName, deviceId, deviceName, houseId, houseName, nextDueDate, status)
- [x] Ajouter méthode `GetUpcomingTasksAsync(Guid userId, int limit)` dans `IMaintenanceService`
- [x] Implémenter dans `MaintenanceService` (query tous les types, calculer nextDueDate, trier, prendre limit)
- [x] Créer endpoint `GET /api/v1/upcoming-tasks?limit=5` dans `UpcomingTasksController`
- [x] Mettre à jour `openapi.yaml` avec le paramètre limit

### Frontend

- [x] Générer client API (hooks manuels, pas de codegen)
- [x] Créer hook `useUpcomingTasks()` dans `src/lib/api/hooks/maintenance.ts`
- [x] Créer composant `UpcomingTasks` intégré dans `src/app/[locale]/(dashboard)/dashboard/page.tsx`
- [x] Intégrer sur la page dashboard (avant liste des maisons)
- [x] Ajouter traductions FR/EN pour "Tâches à venir", "Jamais effectué", etc.

### Tests

- [x] Test intégration: `GetUpcomingTasks_ReturnsTasksSortedByDueDate`
- [x] Test intégration: `GetUpcomingTasks_RespectsLimit`
- [x] Test intégration: `GetUpcomingTasks_OnlyReturnsUserTasks`

---

## Specs techniques

### UpcomingTaskDto
```csharp
public record UpcomingTaskDto(
    Guid TypeId,
    string TypeName,
    Guid DeviceId,
    string DeviceName,
    Guid HouseId,
    string HouseName,
    DateTime? NextDueDate,
    string Status  // "overdue" | "pending" | "up_to_date"
);
```

### Endpoint
```
GET /api/v1/users/upcoming-tasks?limit=5

Response: UpcomingTaskDto[]
```

### Query logic
```csharp
var tasks = await _context.MaintenanceTypes
    .Include(mt => mt.Device).ThenInclude(d => d.House)
    .Include(mt => mt.MaintenanceInstances)
    .Where(mt => mt.Device.House.UserId == userId)
    .ToListAsync();

return tasks
    .Select(mt => CalculateUpcomingTask(mt))
    .OrderBy(t => t.NextDueDate ?? DateTime.MaxValue)
    .Take(limit);
```

---

## Notes

- Réutiliser `CalculateNextDueDate` du `MaintenanceCalculatorService`
- Les tâches sans maintenance (jamais faites) ont `NextDueDate = null` → les mettre en premier (urgentes)
- Afficher "Jamais effectué" si pas de date
