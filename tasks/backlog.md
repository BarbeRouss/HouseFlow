# Backlog

Features non planifiées, issues de la roadmap.

---

## Polish MVP

- [x] Header avec navigation
- [x] Theme toggle (dark/light) dans le header
- [x] Breadcrumb navigation (i18n)
- [x] Locale switcher (FR/EN) dans le header
- [x] Loading states (skeletons)
- [x] Error handling UI (ErrorBoundary)
- [ ] Empty states avec illustrations
- [ ] Tests unitaires frontend

---

## Phase 2: Collaboration

- [ ] Inviter des collaborateurs sur une maison
- [ ] Permissions : lecture seule ou lecture/écriture
- [ ] Accès locataire (vue limitée sans coûts)
- [ ] Gestion des invitations (accepter/refuser)

---

## Phase 3: Notifications

- [ ] Rappels par email (X jours avant échéance)
- [ ] Configuration des préférences de notification
- [ ] Service d'envoi email (SendGrid)
- [ ] Cron job pour vérifier échéances

---

## Phase 4: Premium

- [ ] Entité Organisation (niveau entreprise)
- [ ] Intégration Stripe pour abonnements
- [ ] Gestion des plans (Free/Pro/Enterprise)
- [ ] Fonctionnalités avancées gated

---

## Phase 5: Enrichissement

- [ ] Upload photos/documents (factures, certificats)
- [ ] Statistiques et budgets par maison/appareil
- [ ] Export PDF/CSV des entretiens
- [ ] Suggestions légales par pays/type d'appareil

---

## Infrastructure

- [ ] Déploiement automatique vers le NUC (workflow `deploy.yml`)
  - Configurer l'accès SSH ou un self-hosted runner
  - Docker compose / Aspire publish sur le NUC
  - Déclenché au merge sur `main`

---

## Technical Debt

- [ ] Backend code generation from OpenAPI (currently manual)
- [ ] Loading skeletons instead of "Loading..." text
- [ ] Optimistic UI updates
- [ ] Retry logic for API calls
- [ ] Comprehensive error boundaries

---

## Sécurité & Hardening

Issues identifiées lors de l'audit sécurité du 2026-03-14.
Réf: commit `security: fix CRITICAL injection + harden containers and scripts`

### Priorité Haute
- [ ] Pin GitHub Actions sur SHA au lieu de tags mutables (actions/checkout, docker/login-action, etc.)
- [ ] Séparer les migrations DB du démarrage de l'API (init container ou job CI dédié)
- [ ] Sanitiser les données PII lors du sync prod → preprod (emails, noms, etc.)

### Priorité Moyenne
- [ ] Durcir la CSP : supprimer `unsafe-eval` et `unsafe-inline` de `script-src` (utiliser nonces)
- [ ] Rendre `connect-src` CSP configurable par environnement (actuellement hardcodé localhost)
- [ ] Restreindre CORS `WithMethods` / `WithHeaders` aux seuls verbes et headers utilisés
- [ ] Conditionner PgAdmin à l'environnement Development dans `AppHost/Program.cs`
- [ ] Chiffrer les backups DB (GPG ou age) avant stockage

### Priorité Basse
- [ ] Pinner les images Docker sur digest SHA256 (`node:22-alpine@sha256:...`)
- [ ] Utiliser l'IP client (`X-Forwarded-For`) comme clé de rate limiting au lieu de `Host`
- [ ] Ajouter un pre-flight check dans `setup-vm.sh` qui valide que les `.env` ont été personnalisés

---

**Dernière mise à jour:** 2026-03-14
