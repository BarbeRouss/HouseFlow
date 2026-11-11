# Revue des tests de mise en balance (LIA)

## Contexte

Contrôle de la **revue RGPD {{year}}** (article 32(1)(d) : tester, analyser et évaluer régulièrement l'efficacité des mesures), ouvert automatiquement par le workflow `gdpr-annual-review.yml`. La liste des sept contrôles annuels est au [§ 6.3 de `docs/gdpr/README.md`]({{blob}}/docs/gdpr/README.md#63-revue-annuelle).

Les traitements fondés sur l'intérêt légitime (article 6(1)(f)) reposent sur un test de mise en balance qui doit rester vrai : les durées, les catégories de données ou les finalités ont pu évoluer depuis sa rédaction.

## Mode opératoire

- [Test de mise en balance (LIA)]({{blob}}/docs/gdpr/legitimate-interest-assessment.md) — la section « Revue » du § 4 dresse les cas de réexamen immédiat.

## Prérequis

- Le registre des traitements à jour (le contrôle « concordance registre / schéma réel » de cette même revue peut être fait d'abord).

## Critères d'acceptation

- [ ] Pour chacun des traitements du § 4 (synthèse) : finalité, nécessité et mise en balance ont été relues contre l'état actuel du produit ; toute conclusion qui ne tient plus est réécrite (et, si l'information des personnes change, la politique de confidentialité est mise à jour avec sa version, voir `CLAUDE.md`).
- [ ] Les oppositions reçues dans l'année (tenues hors dépôt dans le journal des demandes) ont été relues : la « suite donnée à une opposition » du tableau de synthèse est toujours la bonne.
- [ ] **Preuve — LIA.** Dans le tableau d'en-tête de [`docs/gdpr/legitimate-interest-assessment.md`]({{blob}}/docs/gdpr/legitimate-interest-assessment.md), la ligne *Date de réalisation* mentionne la revue (`… (revue annuelle AAAA-MM-JJ)`) et la ligne *Version* est incrémentée.
- [ ] **Preuve — tableau de la revue annuelle.** Une ligne est ajoutée au tableau de [`docs/gdpr/README.md` § 6.3]({{blob}}/docs/gdpr/README.md#63-revue-annuelle) (en remplaçant la ligne *« aucune revue à ce jour »* s'il s'agit de la première) :
  `| AAAA-MM-JJ | <opérateur> | Revue du LIA — <constat en une phrase> | <actions décidées, ou « aucune »> |`

## Priorité

`priority:medium` : un contrôle annuel non exécuté est un manquement à l'article 32(1)(d) du RGPD, et un contrôle exécuté mais non daté est indémontrable. Fermer cette issue sans la ligne de preuve ci-dessus ne vaut rien : la clôture n'est valable que si le dossier porte la date.
