# Dossier de conformité RGPD — HouseFlow

**Documentation d'accountability au sens de l'article 5(2) du RGPD**

> L'article 5(2) impose que le responsable du traitement soit non seulement **responsable du respect** des principes de protection des données, mais aussi **en mesure de le démontrer**. Les documents réunis ici constituent cette démonstration. Ils sont versionnés dans le dépôt : leur historique Git fait foi de leur date d'établissement et de leurs évolutions successives.

---

## 1. Identification

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | **Rouss Consulting SRL** (société de droit belge), éditrice du service HouseFlow |
| **Numéro d'entreprise (BCE)** | 0805.984.579 |
| **Numéro de TVA** | BE 0805 984 579 |
| **Adresse postale** | **Non publiée** — décision de l'éditeur : le contact se fait par e-mail. Le siège social reste consultable à la Banque-Carrefour des Entreprises. À réexaminer à l'ouverture de la vente, le commerce électronique imposant alors une adresse géographique accessible. |
| **Contact vie privée / exercice des droits** | `privacy@houseflow.cloud` |
| **Contact sécurité / signalement de vulnérabilité** | `security@houseflow.cloud` |
| **Délégué à la protection des données (DPO)** | **Aucun** — désignation non obligatoire au titre de l'Art. 37(1) ([analyse documentée](./processing-register.md#annexe-a--analyse-de-la-nécessité-de-désigner-un-dpo-art-37)) |
| **Référent vie privée** | Désigné au sein de l'éditeur, joignable à `privacy@houseflow.cloud`. Il tient le registre, suit le journal des demandes, pilote la procédure de violation et conduit la revue annuelle. |
| **Autorité de contrôle chef de file** | **Autorité de protection des données (APD/GBA), Belgique** — l'établissement principal du responsable est en Belgique. Toute personne conserve le droit de saisir l'autorité de son propre État membre de résidence (Art. 77(1)). |
| **Hébergement** | Microsoft Azure, région **West Europe** (Pays-Bas) |
| **Date d'établissement du dossier** | **2026-09-11** |
| **Version** | 1.0 |

---

## 2. Documents du dossier

### Conformité RGPD — `docs/gdpr/`

| Document | Objet | Fondement |
|---|---|---|
| **[processing-register.md](./processing-register.md)** | **Registre des activités de traitement** — 7 fiches de traitement au format du modèle simplifié CNIL, analyse DPO, analyse AIPD, description générale des mesures de sécurité, procédure de mise à jour. | Art. 30 |
| **[legitimate-interest-assessment.md](./legitimate-interest-assessment.md)** | **Test de mise en balance (LIA)** en trois étapes pour les trois traitements fondés sur l'intérêt légitime : journaux d'audit, refresh tokens et adresses IP, invitations. | Art. 6(1)(f), 5(2) |
| **[data-retention-policy.md](./data-retention-policy.md)** | **Politique de conservation** — trois phases du cycle de vie, tableau des durées avec justification et mécanisme de purge, sauvegardes, comptes inactifs, journaux applicatifs, arbitrages documentés, requêtes de contrôle. | Art. 5(1)(e), 5(2), 17 |
| **[subprocessors.md](./subprocessors.md)** | **Sous-traitants et destinataires** — Microsoft Azure, GitHub ; localisation, mécanismes de transfert, couverture des huit obligations de l'Art. 28(3) ; procédure d'ajout d'un sous-traitant. | Art. 28, 44-49 |
| **[rights-requests-log.md](./rights-requests-log.md)** | **Journal des demandes d'exercice de droits** — règles de délai et de gratuité, traitement par droit, procédure en huit étapes, tableau de suivi, modèles de réponse. | Art. 12-22, 5(2) |

### Sécurité — `docs/security/`

| Document | Objet | Fondement |
|---|---|---|
| **[breach-notification-procedure.md](../security/breach-notification-procedure.md)** | **Procédure de notification de violation** — définition, rôles, détection, arbre de décision, délais, commandes de confinement réelles, grille de qualification du risque, modèles de notification à l'autorité et de communication aux personnes (FR/EN), exercice de simulation annuel. | Art. 33, 34 |
| **[breach-register.md](../security/breach-register.md)** | **Registre des violations** — tableau de synthèse et modèle de fiche détaillée. Obligatoire même en l'absence de violation, et couvrant celles qui ne sont pas notifiées. | Art. 33(5) |

### Documents liés hors de ce dossier

| Document | Objet |
|---|---|
| [`SECURITY.md`](../../SECURITY.md) | Mesures de sécurité applicatives et procédure de signalement de vulnérabilité |
| [`specs/openapi.yaml`](../../specs/openapi.yaml) | Contrat d'API, section *USER ACCOUNT & RGPD* — endpoints d'export, de rectification et de suppression |
| Pages `/privacy` et `/terms` | Politique de confidentialité (Art. 13) et conditions générales d'utilisation, publiées dans l'application |
| `infrastructure/terraform/environment/` | Configuration d'infrastructure : région, réseau privé, sauvegardes, rétention des journaux |
| `dbtools/pseudonymize.sql` + `dbtools/verify.sql` | Pseudonymisation obligatoire, et sa vérification bloquante, avant toute copie de production vers un environnement non productif |

---

## 3. Comment lire ce dossier

| Situation | Par où commencer |
|---|---|
| **Contrôle d'une autorité** | [Registre des traitements](./processing-register.md) — c'est le document exigé par l'Art. 30(4), et sa **date de dernière mise à jour** est le premier élément examiné. |
| **Ajout d'une fonctionnalité** | [Definition of done](./processing-register.md#definition-of-done-dune-fonctionnalité-touchant-des-données-personnelles) — les cinq points à satisfaire avant la mise en production. |
| **Demande d'un utilisateur** | [Journal des demandes](./rights-requests-log.md) — délais, procédure, modèles de réponse. |
| **Incident de sécurité** | [Procédure de violation](../security/breach-notification-procedure.md) — **§ 6 en premier** : confiner avant de qualifier. |
| **Ajout d'un service tiers** | [Procédure d'ajout d'un sous-traitant](./subprocessors.md#5-procédure-dajout-dun-sous-traitant) — les cinq étapes à franchir avant toute mise en production. |
| **Question sur une durée de conservation** | [Politique de conservation](./data-retention-policy.md#2-tableau-des-durées-de-conservation) — tableau source unique. |

---

## 4. Principes retenus

Quatre choix structurent l'ensemble du dossier et méritent d'être connus avant toute lecture.

| Principe | Conséquence |
|---|---|
| **Aucun consentement n'est collecté** | Les traitements reposent sur l'**exécution du contrat** (Art. 6(1)(b)) et l'**intérêt légitime** (Art. 6(1)(f)). La case cochée à l'inscription est une **acceptation des CGU** doublée d'une **information** au titre de l'Art. 13 — jamais un consentement au sens de l'Art. 7. Les champs `consentAccepted`, `ConsentGivenAt` et `ConsentPolicyVersion` conservent ce nom pour des raisons historiques, sans que cela n'emporte de qualification juridique. |
| **Aucun traceur non exempté** | Le cookie `refreshToken` et les clés de stockage local `houseflow_session` et `houseflow_theme` sont **strictement nécessaires au service expressément demandé** et donc exemptés de consentement (art. 82 de la loi Informatique et Libertés). **Aucun bandeau cookies n'est affiché** — en afficher un serait trompeur, puisqu'il suggérerait un choix inexistant. L'information reste assurée par une section dédiée de la politique de confidentialité. |
| **Chaque durée est appliquée par un mécanisme automatique** | Une durée écrite sans purge effective est requalifiée en manquement à l'Art. 5(1)(e). Chaque ligne du [tableau des durées](./data-retention-policy.md#2-tableau-des-durées-de-conservation) est associée à un job, une cascade de suppression ou une procédure manuelle datée. |
| **Aucune donnée réelle hors production** | Le dump nocturne est pseudonymisé puis vérifié avant de quitter la production ; `verify.sql` bloque la publication au moindre écart. Seuls les comptes de `preserved_emails` restent intacts : à ce jour le compte du mainteneur et un compte de démonstration fictif, aucune donnée de tiers. |

---

## 5. Arbitrages documentés

L'article 5(2) impose de pouvoir **démontrer** ses choix. Les quatre décisions suivantes ont été arrêtées le **2026-09-11** et sont motivées en détail dans les documents indiqués.

### 5.1 — Décret n° 2021-1362 : non retenu

Le décret du 20 octobre 2021 impose aux **hébergeurs de contenus destinés au public** la conservation pendant un an des données d'identification des contributeurs. **HouseFlow ne publie aucun contenu au public** : les données ne sont accessibles qu'aux membres d'une maison.

**Décision : ne pas se prévaloir de ce décret.** La durée d'un an appliquée aux journaux d'audit est retenue au titre de l'**intérêt légitime de sécurité** (Art. 6(1)(f)), dans la fourchette de 6 mois à 1 an recommandée par la CNIL.

**Conséquence** : les personnes conservent un **droit d'opposition** (Art. 21), traité par une réponse motivée. Invoquer une obligation légale inexistante les en aurait privées.

→ [Motivation complète](./data-retention-policy.md#61--décret-n-2021-1362--non-retenu) · [LIA](./legitimate-interest-assessment.md#1--journaux-daudit)

### 5.2 — Autorité de contrôle chef de file : APD (Belgique)

Le guichet unique (Art. 56) désigne l'autorité de l'**établissement principal** du responsable — ni le lieu de résidence des utilisateurs, ni celui de l'hébergement.

**Décision (2026-09-23) : l'Autorité de protection des données (APD/GBA)**, le responsable Rouss Consulting SRL étant établi en Belgique. Toute personne conserve le droit de saisir l'autorité de **son** État membre de résidence (Art. 77(1)) ; la politique de confidentialité mentionne l'APD et la CNIL à ce titre.

**Le canal de notification en cas de violation est donc celui de l'APD.**

→ [Motivation complète](./data-retention-policy.md#62--autorité-de-contrôle-chef-de-file--à-confirmer)

### 5.3 — Pas de période de grâce : suppression immédiate

Une période de grâce de 30 jours serait licite, mais elle maintiendrait les données après que la personne en a demandé l'effacement, et supposerait un email d'annulation — fonctionnalité qui n'existe pas.

**Décision : suppression immédiate et définitive**, sans période de grâce. Les garde-fous sont d'une autre nature : confirmation explicite dans l'interface, et invitation à exporter ses données au préalable.

**Réserves annoncées** : rotation des sauvegardes (7 jours) et fenêtre résiduelle du JWT d'accès (15 minutes au plus).

→ [Motivation complète](./data-retention-policy.md#63--absence-de-période-de-grâce) · [Effets détaillés](./data-retention-policy.md#4-suppression-de-compte)

### 5.4 — Maisons partagées : transfert au collaborateur le plus ancien

Une maison partagée constitue aussi l'historique des autres membres : la détruire effacerait les données de tiers. La conserver sans propriétaire produirait un enregistrement orphelin, inadministrable.

**Décision** : à la suppression du compte propriétaire, la propriété est **transférée au membre non-locataire le plus ancien** (`CollaboratorRW` prioritaire sur `CollaboratorRO`) ; **à défaut, la maison est supprimée** avec tout son contenu. Les **locataires** sont exclus de la dévolution : leur rôle traduit un accès restreint à un logement qu'ils occupent sans l'administrer.

→ [Motivation complète](./data-retention-policy.md#64--maisons-partagées--transfert-au-collaborateur-le-plus-ancien)

---

## 6. Procédure de mise à jour

### 6.1 Règle fondamentale

> **Le registre des traitements doit être mis à jour à chaque nouveau traitement, à chaque nouvelle catégorie de données et à chaque nouveau sous-traitant — *avant* la mise en production.**

Une fonctionnalité manipulant des données personnelles n'est **pas terminée** tant que les cinq points suivants ne sont pas satisfaits. Ce bloc constitue la **definition of done** de ces fonctionnalités.

- [ ] La fiche concernée du [registre des traitements](./processing-register.md) est créée ou mise à jour.
- [ ] La **date de dernière mise à jour** et la **version** de l'en-tête du registre sont incrémentées.
- [ ] La [politique de conservation](./data-retention-policy.md) est alignée, et la purge correspondante est **implémentée et testée**.
- [ ] La **politique de confidentialité** publiée (`/privacy`) est mise à jour et sa version incrémentée si l'information des personnes est affectée.
- [ ] Si la base retenue est l'**intérêt légitime**, une section est ajoutée au [LIA](./legitimate-interest-assessment.md) ; si un **sous-traitant** est ajouté, sa fiche est créée dans [subprocessors.md](./subprocessors.md) et son DPA archivé.

### 6.2 Événements déclencheurs

| Événement | Documents à mettre à jour |
|---|---|
| Nouvelle finalité ou nouveau traitement | Registre, politique de confidentialité |
| Nouvelle colonne ou table contenant une donnée personnelle | Registre, politique de conservation, politique de confidentialité |
| Nouveau sous-traitant ou outil SaaS | Sous-traitants, registre, politique de confidentialité |
| Changement de base légale | Registre, LIA, politique de confidentialité |
| Changement de durée de conservation | Politique de conservation, registre, `appsettings.json`, politique de confidentialité |
| Nouveau transfert hors EEE | Sous-traitants, registre, politique de confidentialité |
| Ajout d'un traceur non exempté | Politique de confidentialité **et mise en place d'un CMP conforme** |
| Évolution des mesures de sécurité | Registre (annexe C), `SECURITY.md` |
| Violation de données | Registre des violations, et procédure de violation après post-mortem |

### 6.3 Revue annuelle

Conduite par le référent vie privée, **même en l'absence d'évolution fonctionnelle**.

| Contrôle | Document |
|---|---|
| Concordance entre le registre, le schéma de base réel et les durées effectivement appliquées | [Registre](./processing-register.md), [requêtes de contrôle](./data-retention-policy.md#8-contrôle-de-conformité) |
| Revue des tests de mise en balance | [LIA](./legitimate-interest-assessment.md) |
| Validité des DPA et des certifications de transfert | [Sous-traitants](./subprocessors.md) |
| Purge des demandes de plus de 3 ans | [Journal des demandes](./rights-requests-log.md) |
| Test de restauration de sauvegarde, daté | [Politique de conservation § 3.2](./data-retention-policy.md#32-test-de-restauration) |
| Exercice de simulation de violation | [Procédure § 13](../security/breach-notification-procedure.md#13--exercice-de-simulation-annuel) |
| Campagne de suppression des comptes inactifs | [Politique de conservation § 5](./data-retention-policy.md#5-comptes-inactifs) |

| Date de revue | Opérateur | Constats | Actions |
|---|---|---|---|
| *(aucune revue à ce jour)* | | | |

---

## 7. Points à compléter par le responsable

**Tranchés le 2026-09-23** — identité du responsable (**Rouss Consulting SRL**, BCE 0805.984.579,
TVA BE 0805 984 579), autorité de contrôle chef de file (**APD/GBA**, Belgique), contact
(**e-mail uniquement**, `privacy@houseflow.cloud`), droit applicable des CGU (**droit belge**, sans
priver un consommateur des règles impératives de son pays de résidence), **absence de suppléant**
assumée avec la notification par phases comme mesure compensatoire, **conservation des données du
mainteneur** dans les environnements de prévisualisation avec pseudonymisation des champs nommant un
tiers, **qualification d'OVH et d'Anthropic** (ni l'un ni l'autre sous-traitant), et **tenue du
journal des demandes hors dépôt**. Ces valeurs sont propagées dans les six documents du dossier et
dans les quatre pages légales.

| # | Point | Où | Bloquant pour |
|---|---|---|---|
| 1 | **Relecture juridique** des pages `/privacy` et `/terms` | [Pages légales](../../src/HouseFlow.Web/Features/Legal/) | mise en production |
| 2 | **DPA Microsoft** : recopier la version et la date d'acceptation, archiver le PDF hors dépôt | [Sous-traitants § 2.5](./subprocessors.md) — l'emplacement exact où lire chaque valeur y est indiqué | ouverture à des tiers |
| 3 | **DPA GitHub** : idem | [Sous-traitants § 3.3](./subprocessors.md) | ouverture à des tiers |
| 4 | **Vérification annuelle de la certification** de Microsoft sur la *Data Privacy Framework List* — deux minutes, une fois par an, à dater | [Sous-traitants § 2.4](./subprocessors.md) | — (récurrent) |
| 5 | **Test de restauration** de sauvegarde : réaliser et dater | [Politique de conservation § 3.2](./data-retention-policy.md) | — (annuel) |
| 6 | **Exercice de simulation de violation** : réaliser et dater | [Procédure de violation § 13](../security/breach-notification-procedure.md) | — (annuel) |
| 7 | **Adresse géographique** dans les mentions légales — non publiée aujourd'hui, le commerce électronique l'imposera | Pages légales | ouverture de la vente |
| 8 | **Conditions générales de vente**, droit de rétractation, prestataire de paiement au registre, conservation comptable de 7 ans | Registre, pages légales | ouverture de la vente |

---

## 8. Historique des versions

| Version | Date | Modifications |
|---|---|---|
| 1.0 | 2026-09-11 | Constitution du dossier — registre des traitements (Art. 30), LIA, politique de conservation, sous-traitants, journal des demandes, procédure et registre des violations (Art. 33-34). |
