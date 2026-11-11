# Test de restauration de sauvegarde

## Contexte

Contrôle de la **revue RGPD {{year}}** (article 32(1)(d) : tester, analyser et évaluer régulièrement l'efficacité des mesures), ouvert automatiquement par le workflow `gdpr-annual-review.yml`. La liste des sept contrôles annuels est au [§ 6.3 de `docs/gdpr/README.md`]({{blob}}/docs/gdpr/README.md#63-revue-annuelle).

Une sauvegarde jamais restaurée n'est pas une preuve : l'article 32(1)(c) demande de pouvoir rétablir la disponibilité des données, et l'article 32(1)(d) d'en vérifier l'efficacité.

## Mode opératoire

- [Test de restauration — mode opératoire et fiche de preuve]({{blob}}/docs/security/backup-restore-drill.md)

## Prérequis

- Ceux du [§ 3 du mode opératoire]({{blob}}/docs/security/backup-restore-drill.md#3-prérequis) (accès `az`, rôle Contributor sur le resource group de production, administrateur Entra du serveur, clé SSH du bastion, plage de deux heures sans déploiement).
- **Ce contrôle est entièrement manuel et le restera** : le test crée une copie de la production, c'est-à-dire une duplication de données personnelles. Il n'est donc jamais automatisé, et la copie doit être détruite à l'issue (durée maximale d'existence : 4 h).

## Critères d'acceptation

- [ ] Le test a été conduit selon les sept étapes du § 4, et la copie restaurée a été détruite **et** sa destruction vérifiée (étape 7).
- [ ] La fiche de preuve du § 7 a été remplie **pendant** le test.
- [ ] **Preuve — historique.** Une ligne est ajoutée au tableau « Historique des tests » du [§ 7 de `backup-restore-drill.md`]({{blob}}/docs/security/backup-restore-drill.md#7-fiche-de-preuve) : `| AAAA-MM-JJ | <opérateur> | <instant restauré> | <durée de restauration> | <verdict> | <écarts / actions> |`.
- [ ] La date du test est reportée dans la [politique de conservation § 3.2]({{blob}}/docs/gdpr/data-retention-policy.md#32-test-de-restauration).
- [ ] **Preuve — tableau de la revue annuelle.** Une ligne est ajoutée au tableau de [`docs/gdpr/README.md` § 6.3]({{blob}}/docs/gdpr/README.md#63-revue-annuelle) (en remplaçant la ligne *« aucune revue à ce jour »* s'il s'agit de la première) :
  `| AAAA-MM-JJ | <opérateur> | Test de restauration de sauvegarde — <constat en une phrase> | <actions décidées, ou « aucune »> |`

## Priorité

`priority:medium` : un contrôle annuel non exécuté est un manquement à l'article 32(1)(d) du RGPD, et un contrôle exécuté mais non daté est indémontrable. Fermer cette issue sans la ligne de preuve ci-dessus ne vaut rien : la clôture n'est valable que si le dossier porte la date.
