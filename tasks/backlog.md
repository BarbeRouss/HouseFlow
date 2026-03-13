# Backlog

Features non planifiées, issues de la roadmap.

---

## Polish MVP

- [x] Header avec navigation
- [x] Theme toggle (dark/light) dans le header
- [ ] Breadcrumb navigation
- [ ] Locale switcher (FR/EN) dans le header
- [ ] Loading states (skeletons)
- [ ] Error handling UI amélioré
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

**Dernière mise à jour:** 2026-03-12
