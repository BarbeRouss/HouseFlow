# Concordance registre / schéma réel / durées appliquées

## Contexte

Contrôle de la **revue RGPD {{year}}** (article 32(1)(d) : tester, analyser et évaluer régulièrement l'efficacité des mesures), ouvert automatiquement par le workflow `gdpr-annual-review.yml`. La liste des sept contrôles annuels est au [§ 6.3 de `docs/gdpr/README.md`]({{blob}}/docs/gdpr/README.md#63-revue-annuelle).

Le registre des traitements décrit des données, des finalités et des durées ; ce contrôle vérifie qu'elles correspondent encore à ce que la base contient et à ce que la purge applique réellement, même sans évolution fonctionnelle.

## Mode opératoire

- [Registre des traitements — § 4, revue périodique]({{blob}}/docs/gdpr/processing-register.md)
- [Requêtes de contrôle — `data-retention-policy.md` § 8]({{blob}}/docs/gdpr/data-retention-policy.md#8-contrôle-de-conformité) (lectures seules, à lancer sur la production depuis le bastion)

## Prérequis

- Accès en lecture à la base de production (Entra, via le bastion).
- Le schéma réel de la base : migrations appliquées en production et fichiers de `src/HouseFlow.Infrastructure`.

## Critères d'acceptation

- [ ] Chaque fiche de traitement du registre a été comparée au schéma réel (tables et colonnes portant des données personnelles) ; tout écart est corrigé dans le registre **ou** donne lieu à une issue.
- [ ] Les requêtes du § 8 de la politique de conservation ont été lancées et leurs résultats comparés aux valeurs attendues.
- [ ] **Preuve — registre.** Une ligne est ajoutée au tableau « Historique des versions » de [`docs/gdpr/processing-register.md` § 6]({{blob}}/docs/gdpr/processing-register.md#6-historique-des-versions) : `| <version suivante> | AAAA-MM-JJ | Référent vie privée | Revue annuelle {{year}} : concordance registre / schéma / durées — <constat>. |` (la version est incrémentée même sans autre modification).
- [ ] **Preuve — contrôle de conformité.** Une ligne est ajoutée au tableau de `data-retention-policy.md` § 8 : `| AAAA-MM-JJ | <opérateur> | <résultat des requêtes> | <anomalies et suites données> |`.
- [ ] **Preuve — tableau de la revue annuelle.** Une ligne est ajoutée au tableau de [`docs/gdpr/README.md` § 6.3]({{blob}}/docs/gdpr/README.md#63-revue-annuelle) (en remplaçant la ligne *« aucune revue à ce jour »* s'il s'agit de la première) :
  `| AAAA-MM-JJ | <opérateur> | Concordance registre / schéma / durées — <constat en une phrase> | <actions décidées, ou « aucune »> |`

## Priorité

`priority:medium` : un contrôle annuel non exécuté est un manquement à l'article 32(1)(d) du RGPD, et un contrôle exécuté mais non daté est indémontrable. Fermer cette issue sans la ligne de preuve ci-dessus ne vaut rien : la clôture n'est valable que si le dossier porte la date.
