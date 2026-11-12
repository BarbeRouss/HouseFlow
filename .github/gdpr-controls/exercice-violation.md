# Exercice de simulation de violation de données

> **⚠️ Prérequis bloquant : le compte sur le guichet de notification de l'APD doit exister, et son accès être vérifié, AVANT l'exercice.** C'est le point qui échoue le plus souvent le jour J, et le créer en urgence pendant les 72 heures est exactement ce que l'exercice doit éviter. Si le compte n'existe pas, le créer est la première tâche de cette issue ; ne pas commencer l'exercice sans lui.

## Contexte

Contrôle de la **revue RGPD {{year}}** (article 32(1)(d) : tester, analyser et évaluer régulièrement l'efficacité des mesures), ouvert automatiquement par le workflow `gdpr-annual-review.yml`. La liste des sept contrôles annuels est au [§ 6.3 de `docs/gdpr/README.md`]({{blob}}/docs/gdpr/README.md#63-revue-annuelle).

L'article 32(1)(d) impose d'éprouver les mesures ; une procédure de notification jamais jouée échoue le jour où elle sert. L'exercice est **sur table** : aucune action réelle en production.

## Mode opératoire

- [Procédure de notification de violation — § 13, exercice annuel]({{blob}}/docs/security/breach-notification-procedure.md#13--exercice-de-simulation-annuel) ; préparation et déroulé au § 13.0, scénario au § 13.1, checklist au § 13.2, compte rendu au § 13.3.

## Prérequis

- Le compte sur le guichet de notification de l'APD (voir l'avertissement en tête).
- Une demi-journée continue sans déploiement prévu, et les prérequis de la préparation du § 13.0 (accès Azure, clé SSH du bastion, modèles § 9.2 et § 10.4, minuteur).

## Critères d'acceptation

- [ ] L'exercice a été conduit selon le déroulé du § 13.0, le minuteur arrêté, et chaque étape non exécutée jusqu'à son livrable écrit est consignée comme **non éprouvée**.
- [ ] La fiche fictive ouverte au [registre des violations]({{blob}}/docs/security/breach-register.md) pendant l'exercice a été **retirée**.
- [ ] La procédure a été corrigée, dans la journée, pour chaque erreur trouvée.
- [ ] **Preuve — compte rendu.** Le tableau du [§ 13.3 de `breach-notification-procedure.md`]({{blob}}/docs/security/breach-notification-procedure.md#133-compte-rendu-dexercice) est rempli et une ligne est ajoutée à l'historique : `| AAAA-MM-JJ | <scénario> | <délai simulé> | <écarts identifiés> | <actions correctives> |` ; les lignes *Dernier exercice réalisé* et *Prochain exercice prévu* du tableau d'en-tête du § 13 sont mises à jour.
- [ ] **Preuve — tableau de la revue annuelle.** Une ligne est ajoutée au tableau de [`docs/gdpr/README.md` § 6.3]({{blob}}/docs/gdpr/README.md#63-revue-annuelle) (en remplaçant la ligne *« aucune revue à ce jour »* s'il s'agit de la première) :
  `| AAAA-MM-JJ | <opérateur> | Exercice de simulation de violation — délai simulé <durée> — <constat en une phrase> | <actions décidées, ou « aucune »> |`

## Priorité

`priority:medium` : un contrôle annuel non exécuté est un manquement à l'article 32(1)(d) du RGPD, et un contrôle exécuté mais non daté est indémontrable. Fermer cette issue sans la ligne de preuve ci-dessus ne vaut rien : la clôture n'est valable que si le dossier porte la date.
