# Lessons Learned

Patterns et erreurs à éviter, capturés après corrections.

---

## 2026-03-12

### Sprint créé sans User Story correspondante
**Contexte:** Feature "Prochaines tâches" ajoutée directement dans sprint.md sans être dans user-stories.md.
**Cause:** Workflow incomplet - on a sauté l'étape d'ajout aux specs.
**Leçon:** TOUJOURS ajouter une US dans `specs/user-stories.md` AVANT de créer un sprint. Le sprint référence les US, pas l'inverse.

### Tests InMemory ne détectent pas les migrations manquantes
**Contexte:** L'API refusait de démarrer avec PendingModelChangesWarning, mais les tests passaient.
**Cause:** Les tests d'intégration utilisent `UseEnvironment("Testing")` avec base InMemory qui ne vérifie pas les migrations.
**Leçon:** Toujours vérifier que les migrations sont à jour avant de démarrer Aspire. Commande: `dotnet ef migrations list`.

### Port 22222 occupé après arrêt brutal d'Aspire
**Contexte:** Aspire refuse de démarrer car le port 22222 est occupé.
**Cause:** Le processus DCP d'Aspire n'a pas été arrêté proprement.
**Leçon:** Tuer le processus manuellement: `netstat -ano | findstr :22222` puis `taskkill /PID <PID> /F`.

---

## Template

### [Titre court du problème]
**Contexte:** Qu'est-ce qui s'est passé ?
**Cause:** Pourquoi c'est arrivé ?
**Leçon:** Comment éviter à l'avenir ?
