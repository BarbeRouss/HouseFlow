# Campagne de suppression des comptes inactifs

## Contexte

Contrôle de la **revue RGPD {{year}}** (article 32(1)(d) : tester, analyser et évaluer régulièrement l'efficacité des mesures), ouvert automatiquement par le workflow `gdpr-annual-review.yml`. La liste des sept contrôles annuels est au [§ 6.3 de `docs/gdpr/README.md`]({{blob}}/docs/gdpr/README.md#63-revue-annuelle).

Un compte sans activité depuis plus de 3 ans n'a plus de finalité qui justifie sa conservation (article 5(1)(e)). La campagne identifie ces comptes, les prévient, puis les supprime selon la logique du droit à l'effacement.

## Mode opératoire

- [Politique de conservation § 5 — comptes inactifs]({{blob}}/docs/gdpr/data-retention-policy.md#5-comptes-inactifs) : identification (§ 5.1), préavis de 30 jours (§ 5.2), suppression (§ 5.3).

## Prérequis

- Accès en lecture à la base de production pour la requête d'identification.
- L'envoi d'e-mails transactionnels n'étant pas implémenté, **le préavis est envoyé à la main** depuis la messagerie de l'éditeur, et **aucun compte ne peut être supprimé sans préavis préalable** : la suppression a lieu au moins 30 jours après l'envoi. Cette issue peut donc rester ouverte plusieurs semaines ; c'est normal.

## Critères d'acceptation

- [ ] La requête d'identification du § 5.1 a été lancée, ainsi que son contrôle de cohérence (qui doit renvoyer zéro ligne).
- [ ] Les préavis ont été envoyés, et la suppression n'a eu lieu que pour les comptes sans connexion entre-temps (les comptes sous limitation de traitement, article 18, ne sont jamais purgés).
- [ ] **Preuve — campagne.** Une ligne est ajoutée au tableau du [§ 5.3 de `data-retention-policy.md`]({{blob}}/docs/gdpr/data-retention-policy.md#53-étape-3--suppression) : `| AAAA-MM-JJ | <comptes identifiés> | <préavis envoyés> | <réactivés> | <supprimés> | <opérateur> |` (ligne à écrire même si aucun compte n'était concerné : « 0 / 0 / 0 / 0 »).
- [ ] **Preuve — tableau de la revue annuelle.** Une ligne est ajoutée au tableau de [`docs/gdpr/README.md` § 6.3]({{blob}}/docs/gdpr/README.md#63-revue-annuelle) (en remplaçant la ligne *« aucune revue à ce jour »* s'il s'agit de la première) :
  `| AAAA-MM-JJ | <opérateur> | Campagne de suppression des comptes inactifs — <constat en une phrase> | <actions décidées, ou « aucune »> |`

## Priorité

`priority:medium` : un contrôle annuel non exécuté est un manquement à l'article 32(1)(d) du RGPD, et un contrôle exécuté mais non daté est indémontrable. Fermer cette issue sans la ligne de preuve ci-dessus ne vaut rien : la clôture n'est valable que si le dossier porte la date.
