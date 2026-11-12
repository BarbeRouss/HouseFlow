# Purge des demandes d'exercice de droits de plus de 3 ans

## Contexte

Contrôle de la **revue RGPD {{year}}** (article 32(1)(d) : tester, analyser et évaluer régulièrement l'efficacité des mesures), ouvert automatiquement par le workflow `gdpr-annual-review.yml`. La liste des sept contrôles annuels est au [§ 6.3 de `docs/gdpr/README.md`]({{blob}}/docs/gdpr/README.md#63-revue-annuelle).

Le journal des demandes d'exercice de droits est conservé 3 ans à compter de la réponse (article 5(1)(e)). Passé ce délai, les lignes doivent disparaître.

## Mode opératoire

- [Journal des demandes — durée de conservation et instructions de tenue]({{blob}}/docs/gdpr/rights-requests-log.md)

## Prérequis

- Accès au journal **tenu hors dépôt**, dans l'espace documentaire de Rouss Consulting SRL (le fichier du dépôt n'en est que le modèle).

## Critères d'acceptation

- [ ] Les lignes dont la date de réponse remonte à plus de 3 ans ont été supprimées du journal hors dépôt (ou le journal ne contient aucune ligne de cet âge).
- [ ] Les éventuelles demandes de limitation (article 18) encore actives, qui suspendent la purge, ont été exclues de la suppression.
- [ ] **Aucune donnée personnelle n'est recopiée dans cette issue ni dans le dépôt** : seul le nombre de lignes purgées est consigné.
- [ ] **Preuve.** Le journal étant hors dépôt, la seule trace du dépôt est la ligne du tableau de revue annuelle, qui indique le nombre de lignes purgées.
- [ ] **Preuve — tableau de la revue annuelle.** Une ligne est ajoutée au tableau de [`docs/gdpr/README.md` § 6.3]({{blob}}/docs/gdpr/README.md#63-revue-annuelle) (en remplaçant la ligne *« aucune revue à ce jour »* s'il s'agit de la première) :
  `| AAAA-MM-JJ | <opérateur> | Purge du journal des demandes — <n> ligne(s) supprimée(s) — <constat en une phrase> | <actions décidées, ou « aucune »> |`

## Priorité

`priority:medium` : un contrôle annuel non exécuté est un manquement à l'article 32(1)(d) du RGPD, et un contrôle exécuté mais non daté est indémontrable. Fermer cette issue sans la ligne de preuve ci-dessus ne vaut rien : la clôture n'est valable que si le dossier porte la date.
