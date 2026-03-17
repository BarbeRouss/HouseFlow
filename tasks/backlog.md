# Backlog

Features non planifiées, trackées dans [GitHub Issues](https://github.com/BarbeRouss/HouseFlow/issues).
Chaque tâche a une issue GitHub associée — les PRs ferment les issues automatiquement via `Closes #XX`.

---

## Polish MVP — [Milestone](https://github.com/BarbeRouss/HouseFlow/milestone/1)

- [x] Header avec navigation
- [x] Theme toggle (dark/light) dans le header
- [x] Breadcrumb navigation (i18n)
- [x] Locale switcher (FR/EN) dans le header
- [x] Loading states (skeletons)
- [x] Error handling UI (ErrorBoundary)
- [ ] Empty states avec illustrations (#20)
- [ ] Tests unitaires frontend (#21)

---

## Phase 2: Collaboration — [Milestone](https://github.com/BarbeRouss/HouseFlow/milestone/2)

- [ ] Inviter des collaborateurs sur une maison (#22)
- [ ] Permissions : lecture seule ou lecture/écriture (#23)
- [ ] Accès locataire (vue limitée sans coûts) (#24)
- [ ] Gestion des invitations (accepter/refuser) (#25)

---

## Phase 3: Notifications — [Milestone](https://github.com/BarbeRouss/HouseFlow/milestone/3)

- [ ] Rappels par email (X jours avant échéance) (#26)
- [ ] Configuration des préférences de notification (#27)
- [ ] Service d'envoi email (SendGrid) (#28)
- [ ] Cron job pour vérifier échéances (#29)

---

## Phase 4: Premium — [Milestone](https://github.com/BarbeRouss/HouseFlow/milestone/4)

- [ ] Entité Organisation (niveau entreprise) (#30)
- [ ] Intégration Stripe pour abonnements (#31)
- [ ] Gestion des plans (Free/Pro/Enterprise) (#32)
- [ ] Fonctionnalités avancées gated (#33)

---

## Phase 5: Enrichissement — [Milestone](https://github.com/BarbeRouss/HouseFlow/milestone/5)

- [ ] Upload photos/documents (factures, certificats) (#34)
- [ ] Statistiques et budgets par maison/appareil (#35)
- [ ] Export PDF/CSV des entretiens (#36)
- [ ] Suggestions légales par pays/type d'appareil (#37)

---

## Infrastructure — [Milestone](https://github.com/BarbeRouss/HouseFlow/milestone/6)

- [ ] Déploiement automatique vers le NUC (#38)

---

## Technical Debt — [Milestone](https://github.com/BarbeRouss/HouseFlow/milestone/7)

- [ ] Backend code generation from OpenAPI (#39)
- [ ] Loading skeletons instead of "Loading..." text (#40)
- [ ] Optimistic UI updates (#41)
- [ ] Retry logic for API calls (#42)
- [ ] Comprehensive error boundaries (#43)

---

## Sécurité & Hardening — [Milestone](https://github.com/BarbeRouss/HouseFlow/milestone/8)

Issues identifiées lors de l'audit sécurité du 2026-03-14.
Réf: commit `security: fix CRITICAL injection + harden containers and scripts`

### Priorité Haute
- [ ] Pin GitHub Actions sur SHA au lieu de tags mutables (#44)
- [ ] Séparer les migrations DB du démarrage de l'API (#45)
- [ ] Sanitiser les données PII lors du sync prod → preprod (#46)

### Priorité Moyenne
- [ ] Durcir la CSP : supprimer `unsafe-eval` et `unsafe-inline` (#47)
- [ ] Rendre `connect-src` CSP configurable par environnement (#48)
- [ ] Restreindre CORS aux seuls verbes et headers utilisés (#49)
- [ ] Conditionner PgAdmin à l'environnement Development (#50)
- [ ] Chiffrer les backups DB avant stockage (#51)

### Priorité Basse
- [ ] Pinner les images Docker sur digest SHA256 (#52)
- [ ] Rate limiting par IP client au lieu de Host (#53)
- [ ] Pre-flight check dans setup-vm.sh (#54)

---

**Dernière mise à jour:** 2026-03-17
