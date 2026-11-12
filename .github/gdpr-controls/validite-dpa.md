# Validité des DPA et des certifications de transfert

## Contexte

Contrôle de la **revue RGPD {{year}}** (article 32(1)(d) : tester, analyser et évaluer régulièrement l'efficacité des mesures), ouvert automatiquement par le workflow `gdpr-annual-review.yml`. La liste des sept contrôles annuels est au [§ 6.3 de `docs/gdpr/README.md`]({{blob}}/docs/gdpr/README.md#63-revue-annuelle).

Le contrat de sous-traitance (DPA Microsoft) et la certification de Microsoft au Data Privacy Framework (DPF) fondent les transferts hors EEE. L'un et l'autre peuvent changer ou être retirés ; la liste officielle fait foi, pas le DPA.

## Mode opératoire

- [Sous-traitants § 2.4 — transferts hors EEE]({{blob}}/docs/gdpr/subprocessors.md#24-transferts-hors-eee) : vérification annuelle de la certification DPF.
- [Sous-traitants § 2.5 — contrat de sous-traitance]({{blob}}/docs/gdpr/subprocessors.md#25-contrat-de-sous-traitance-art-283) : édition du DPA en vigueur et couverture de l'article 28(3).

## Prérequis

- **Un navigateur.** La [Data Privacy Framework List](https://www.dataprivacyframework.gov/list) est une application JavaScript : elle **se consulte à la main** et ne peut être relevée par un script ni par un agent. Deux minutes, une fois par an.
- La veille du DPA Microsoft est **continue** (issue #335, hors de ce contrôle) : ce contrôle-ci ne la remplace pas, il vérifie l'état au moment de la revue.

## Critères d'acceptation

- [ ] Microsoft figure sur la liste DPF, certification **active**.
- [ ] L'édition du DPA relevée au § 2.5 est toujours la dernière publiée par Microsoft ; sinon, les différences sont relues et le § 2.5 est mis à jour.
- [ ] Aucun nouveau sous-traitant ultérieur de Microsoft n'a été annoncé pour les services utilisés par HouseFlow (sinon : mise à jour du § 2 et de la politique de confidentialité).
- [ ] **Preuve — DPF.** Une ligne est ajoutée au tableau de vérification du [§ 2.4 de `docs/gdpr/subprocessors.md`]({{blob}}/docs/gdpr/subprocessors.md#24-transferts-hors-eee) : `| AAAA-MM-JJ | <constat sur la liste officielle : active / retirée / expirée> | <vérificateur> |`.
- [ ] **Preuve — tableau de la revue annuelle.** Une ligne est ajoutée au tableau de [`docs/gdpr/README.md` § 6.3]({{blob}}/docs/gdpr/README.md#63-revue-annuelle) (en remplaçant la ligne *« aucune revue à ce jour »* s'il s'agit de la première) :
  `| AAAA-MM-JJ | <opérateur> | Validité des DPA et du DPF — <constat en une phrase> | <actions décidées, ou « aucune »> |`

## Priorité

`priority:medium` : un contrôle annuel non exécuté est un manquement à l'article 32(1)(d) du RGPD, et un contrôle exécuté mais non daté est indémontrable. Fermer cette issue sans la ligne de preuve ci-dessus ne vaut rien : la clôture n'est valable que si le dossier porte la date.
