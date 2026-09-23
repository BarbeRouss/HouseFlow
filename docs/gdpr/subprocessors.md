# Sous-traitants et destinataires

**Articles 28 et 44-49 du RGPD**

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | HouseFlow (éditeur : BarbeRouss) |
| **Contact vie privée** | `privacy@houseflow.cloud` |
| **Date** | 2026-09-11 |
| **Version** | 1.0 |
| **Revue** | annuelle, et à chaque ajout ou changement de sous-traitant |

> **Rappel.** L'article 28(1) interdit de faire appel à un sous-traitant qui ne présenterait pas de **garanties suffisantes**, et l'article 28(3) impose un **contrat écrit** couvrant huit obligations précises. Répondre « notre hébergeur est conforme au RGPD » n'exonère de rien : la conformité incombe au **responsable de traitement**, qui doit démontrer avoir choisi, encadré et vérifié chacun de ses sous-traitants.

---

## 1. Vue d'ensemble

| Sous-traitant | Rôle | Données traitées | Localisation | Mécanisme de transfert | DPA |
|---|---|---|---|---|---|
| [**Microsoft Azure**](#2-microsoft-azure) | Hébergement, base de données, réseau, journaux de plateforme | **Toutes** les données applicatives | West Europe (Pays-Bas) | Adéquation EU-US DPF + CCT (pour le support hors EEE) | Microsoft Products and Services DPA — *[à archiver : date/version à compléter]* |
| [**GitHub**](#3-github) | Dépôt de code, CI/CD, registre d'images de conteneurs | **Aucune donnée personnelle d'utilisateur** | États-Unis / mondial | Adéquation EU-US DPF + CCT | GitHub Data Protection Agreement — *[à archiver : date/version à compléter]* |

**Deux autres destinataires techniques restent à qualifier** — point ouvert n° 9 du [registre](./processing-register.md#5-points-ouverts) : **OVH**, qui héberge les zones DNS du service, et **Anthropic**, dont le workflow `.github/workflows/claude-issue.yml` déclenche une session d'agent qui lit le dépôt. En dehors d'eux, HouseFlow n'utilise :

- **aucun outil d'analytics** (ni Google Analytics, ni Matomo, ni Plausible, ni aucun équivalent) ;
- **aucun fournisseur d'emailing transactionnel** (l'envoi d'emails n'est pas implémenté) ;
- **aucun outil de support ou de ticketing tiers** (le support est assuré par messagerie directe) ;
- **aucun outil de supervision ou d'APM tiers** (ni Sentry, ni Datadog, ni New Relic) ;
- **aucun CDN, aucun service de paiement, aucun réseau publicitaire.**

Cette sobriété est **délibérée** : elle réduit mécaniquement la surface de conformité, supprime toute question de transfert hors EEE au-delà des deux fournisseurs listés, et fonde l'absence de bandeau cookies — HouseFlow ne dépose aucun traceur non exempté.

---

## 2. Microsoft Azure

### 2.1 Identification

| Élément | Valeur |
|---|---|
| **Entité contractante pour l'EEE** | Microsoft Ireland Operations Limited — One Microsoft Place, South County Business Park, Leopardstown, Dublin 18, Irlande |
| **Qualification** | **Sous-traitant** au sens de l'Art. 4(8) |
| **Rôle** | Hébergement de l'ensemble de l'infrastructure |

### 2.2 Services utilisés

| Service Azure | Usage | Données concernées |
|---|---|---|
| **Azure Container Apps** | Exécution de l'API .NET | Données en transit et en mémoire |
| **Azure Static Web Apps** | Service du frontend Blazor (fichiers statiques) | Aucune : le frontend s'exécute dans le navigateur et appelle l'API directement |
| **Azure Database for PostgreSQL Flexible Server 16** | Base de données de production | **Toutes** les données applicatives : comptes, maisons, équipements, entretiens, adhésions, invitations, jetons, clés API, journaux d'audit |
| **Azure Virtual Network** (`vnet-houseflow`) | Cloisonnement réseau, sous-réseaux délégués `snet-apps` et `snet-db`, zone DNS privée | Métadonnées réseau |
| **Azure Log Analytics** (`law-houseflow`) | Journaux de plateforme et journaux applicatifs, rétention 30 jours | Journaux applicatifs — **sans donnée personnelle** |
| **Azure Storage** | État Terraform (`sthouseflowtfstate`) **et conteneur `db-dumps`** (dump nocturne de la production, voir [TR-07](./processing-register.md#traitement-n-7--environnements-techniques-prévisualisations-de-pull-request)) | État Terraform : aucune. `db-dumps` : copie **pseudonymisée** de la base, dont les comptes de `preserved_emails` restent intacts |
| **Microsoft Entra ID** | Authentification sans mot de passe vers PostgreSQL, identités managées, OIDC pour le déploiement | Identités administratives, non utilisateurs |

### 2.3 Localisation des données

| Élément | Valeur |
|---|---|
| **Région** | **West Europe** — `var.location = "westeurope"`, centres de données situés aux **Pays-Bas** |
| **Redondance géographique des sauvegardes** | **Désactivée** — `geo_redundant_backup_enabled = false`. Les sauvegardes demeurent dans la région West Europe. |
| **Exposition réseau** | La base de données n'est **pas accessible depuis l'Internet public** : `public_network_access_enabled = false`, intégration VNet par sous-réseau délégué et zone DNS privée. |

**Conséquence.** Les données au repos et en traitement demeurent **intégralement dans l'EEE**. Si la redondance géographique venait à être activée, la région appairée de West Europe est **North Europe** (Irlande) — également située dans l'EEE. Aucun transfert hors EEE ne résulterait donc de ce changement, qui devrait néanmoins être consigné ici.

### 2.4 Transferts hors EEE

Un transfert hors EEE demeure possible par un canal résiduel : le **support technique Microsoft**, susceptible d'être assuré depuis un pays tiers, et certaines opérations de télémétrie de la plateforme.

Ces transferts sont encadrés par une double garantie :

| Mécanisme | Fondement | Portée |
|---|---|---|
| **Décision d'adéquation EU-US Data Privacy Framework** du 10 juillet 2023 | **Art. 45** | Microsoft figure sur la *Data Privacy Framework List*. Les transferts vers ses entités américaines certifiées peuvent se fonder sur l'adéquation, sans instrument supplémentaire. |
| **Clauses contractuelles types** de la Commission du 4 juin 2021 | **Art. 46(2)(c)** | Intégrées au DPA Microsoft. Elles sont conservées comme **solution de repli** : l'invalidation successive du Safe Harbor (2015) puis du Privacy Shield (2020) commande de ne pas dépendre d'une seule décision d'adéquation. |

**Vérification périodique requise.** La certification DPF est renouvelée annuellement et peut être retirée. Sa validité doit être **vérifiée et consignée chaque année** sur la [Data Privacy Framework List](https://www.dataprivacyframework.gov/list).

| Date de vérification | Certification DPF de Microsoft | Vérificateur |
|---|---|---|
| *(aucune vérification consignée à ce jour)* | | |

### 2.5 Contrat de sous-traitance (Art. 28(3))

Le **Microsoft Products and Services Data Protection Addendum (DPA)** est le document contractuel applicable. Il est automatiquement incorporé aux conditions des services Azure et couvre les huit obligations de l'article 28(3).

| Obligation Art. 28(3) | Couverture par le DPA Microsoft |
|---|---|
| **(a)** Traitement sur **instruction documentée** du responsable, y compris pour les transferts | Oui — Microsoft s'engage à ne traiter les *Customer Data* que sur instruction documentée du client. |
| **(b)** Engagement de **confidentialité** des personnes autorisées | Oui. |
| **(c)** Mise en œuvre des mesures de l'**Art. 32** | Oui — mesures techniques et organisationnelles décrites en annexe, certifications ISO/IEC 27001, 27017, 27018, SOC 1/2/3. |
| **(d)** Conditions de recours à un **sous-traitant ultérieur** | Oui — liste publique des sous-traitants ultérieurs Microsoft, avec préavis en cas d'ajout et droit d'objection. |
| **(e)** **Assistance** pour répondre aux demandes d'exercice des droits | Oui. |
| **(f)** **Assistance** au respect des Art. 32 à 36, dont la **notification des violations** | Oui — Microsoft s'engage à notifier le responsable **sans retard injustifié** après avoir pris connaissance d'une violation. Voir la [procédure de notification de violation](../security/breach-notification-procedure.md). |
| **(g)** **Suppression ou restitution** des données en fin de prestation | Oui — suppression dans un délai déterminé après la fin de l'abonnement. |
| **(h)** Mise à disposition des informations nécessaires aux **audits** | Oui — rapports d'audit indépendants mis à disposition. |

| Élément d'accountability | État |
|---|---|
| **Version du DPA acceptée** | *[à compléter]* |
| **Date d'acceptation** | *[à compléter]* |
| **Copie archivée** | *[à compléter — archiver le PDF hors dépôt, dans l'espace documentaire du responsable]* |
| **Référence publique** | [Microsoft Products and Services Data Protection Addendum](https://www.microsoft.com/licensing/docs/view/Microsoft-Products-and-Services-Data-Protection-Addendum-DPA) |
| **Sous-traitants ultérieurs** | [Liste publique des sous-traitants Microsoft](https://www.microsoft.com/licensing/docs/view/Microsoft-Online-Services-Subprocessor-List) |

---

## 3. GitHub

### 3.1 Identification et rôle

| Élément | Valeur |
|---|---|
| **Entité** | GitHub, Inc. (filiale de Microsoft Corporation) |
| **Qualification** | **Sous-traitant** pour les seules données du dépôt et de la CI. Pour les comptes des contributeurs, GitHub agit comme **responsable de traitement autonome** — ces personnes ont leur propre relation contractuelle avec GitHub, hors du périmètre de HouseFlow. |
| **Usages** | Hébergement du dépôt de code source ; exécution de la CI/CD (GitHub Actions) ; hébergement des images de conteneurs (GitHub Container Registry) ; suivi des issues et du Project. |

### 3.2 Données concernées

> **Aucune donnée personnelle d'utilisateur de HouseFlow n'est traitée par GitHub.**

| Donnée | Présente chez GitHub ? |
|---|---|
| Code source, spécifications, documentation (dont le présent dossier) | **Oui** |
| Identités des contributeurs (comptes GitHub) | **Oui** — relation directe entre le contributeur et GitHub |
| Secrets de déploiement | **Oui**, sous forme chiffrée. L'authentification vers Azure repose sur **OIDC**, sans secret de longue durée (`id-token: write`). |
| Artefacts de compilation et images de conteneurs | **Oui** — aucune donnée applicative n'y figure |
| **Comptes, maisons, équipements, entretiens, journaux d'audit des utilisateurs** | **NON** |
| **Base de données de production ou toute copie de celle-ci** | **NON** |

**Règle de gouvernance impérative.** Aucun export, capture, extrait ou copie de la base de production **ne doit jamais** être versé dans le dépôt, joint à une issue ou à une pull request, ni déposé comme artefact de CI. Les environnements de prévisualisation de PR sont alimentés par un dump nocturne **pseudonymisé et vérifié avant de quitter la production** (voir la fiche TR-07 du [registre](./processing-register.md#traitement-n-7--environnements-techniques-prévisualisations-de-pull-request)).

### 3.3 Transferts et encadrement

| Élément | Valeur |
|---|---|
| **Localisation** | États-Unis, avec infrastructure mondiale. Les exécutions de GitHub Actions ne sont pas garanties dans l'EEE. |
| **Mécanisme de transfert** | Décision d'adéquation **EU-US Data Privacy Framework** (Art. 45) — GitHub est couvert par la certification de Microsoft Corporation — et **clauses contractuelles types** du 4 juin 2021 (Art. 46(2)(c)) en solution de repli. |
| **Analyse d'impact des transferts (TIA)** | **Non requise** : l'adéquation DPF dispense d'une TIA. Une TIA serait par ailleurs sans objet en l'absence de donnée personnelle d'utilisateur transférée. |
| **DPA** | GitHub Data Protection Agreement — *[à archiver : date/version à compléter]* |

### 3.4 Mesures de sécurité applicables

| Mesure | État |
|---|---|
| Authentification OIDC vers Azure, sans secret de longue durée | Actif — `permissions: id-token: write` |
| Actions GitHub épinglées par SHA de commit | Actif — protection contre la compromission d'une action tierce |
| Images de base Docker épinglées par empreinte SHA-256 | Actif |
| Permissions minimales des workflows | Actif — `permissions: contents: read` par défaut |
| Analyse de secrets et alertes de vulnérabilité | **À activer** — voir les [points ouverts du registre](./processing-register.md#5-points-ouverts) |

---

## 4. Autres destinataires

| Destinataire | Nature | Conditions |
|---|---|---|
| **Membres d'une maison partagée** | Destinataires au sein du service | Un utilisateur qui partage une maison rend ses données de maintenance visibles aux membres qu'il invite, selon leur rôle et leurs permissions. Il s'agit d'une conséquence directe et voulue d'une fonctionnalité qu'il active lui-même. |
| **Autorités judiciaires** | Destinataire légal | Communication sur réquisition régulière uniquement, dans la stricte limite de la demande. Toute réquisition est consignée au [journal des demandes](./rights-requests-log.md). |
| **Autorité de contrôle** (CNIL ou APD) | Destinataire légal | Communication dans le cadre d'une notification de violation (Art. 33), d'une réclamation (Art. 77) ou d'un contrôle (Art. 58). |

**Aucune donnée n'est vendue, louée, échangée ou communiquée à des fins commerciales, publicitaires ou statistiques à un tiers.**

---

## 5. Procédure d'ajout d'un sous-traitant

Un nouveau sous-traitant ne peut être mis en production qu'après avoir franchi **l'intégralité** des étapes ci-dessous. Cette procédure s'applique à tout service tiers traitant des données personnelles pour le compte de HouseFlow — y compris un outil apparemment anodin : un fournisseur d'emailing, un widget de support, une bibliothèque de mesure d'audience, un service de capture d'erreurs.

### Étape 1 — Évaluation préalable (Art. 28(1))

- [ ] Le sous-traitant présente-t-il des **garanties suffisantes** ? Vérifier : certifications (ISO 27001, SOC 2), politique de sécurité publiée, historique d'incidents, ancienneté et solidité du fournisseur.
- [ ] Quelles **catégories de données** lui seront transmises ? Appliquer la minimisation (Art. 5(1)(c)) : ne transmettre que le strict nécessaire.
- [ ] Existe-t-il une **alternative hébergée dans l'EEE**, ou une alternative permettant de ne transmettre aucune donnée personnelle ? Si oui, la privilégier et documenter le choix.

### Étape 2 — Contrat (Art. 28(3))

- [ ] Un **DPA** est signé ou accepté, couvrant les **huit obligations (a) à (h)** de l'article 28(3).
- [ ] Le DPA prévoit explicitement la **notification au responsable en cas de violation** (Art. 28(3)(f) et 33(2)) — point le plus fréquemment absent des contrats standards.
- [ ] Le DPA prévoit la **suppression ou la restitution** des données en fin de prestation (Art. 28(3)(g)).
- [ ] La **liste des sous-traitants ultérieurs** est connue, ainsi que le mécanisme d'information préalable en cas d'ajout (Art. 28(2)).
- [ ] Le DPA est **archivé** (PDF, date, version) dans l'espace documentaire du responsable.

### Étape 3 — Transferts (Art. 44-49)

- [ ] La **localisation effective** de l'hébergement et des traitements est identifiée, service par service.
- [ ] Si des données quittent l'EEE, le **mécanisme de transfert** est établi :
  - **décision d'adéquation** (Art. 45) — vérifier la certification sur la liste officielle et prévoir sa vérification annuelle ; **ou**
  - **clauses contractuelles types** du 4 juin 2021 (Art. 46(2)(c)), **assorties d'une analyse d'impact des transferts (TIA)** documentant le droit du pays destinataire et les mesures supplémentaires retenues.
- [ ] Les **dérogations de l'article 49** ne sont pas invoquées pour un transfert régulier et systématique : elles sont réservées à des cas exceptionnels.

### Étape 4 — Documentation

- [ ] Une **fiche sous-traitant** est ajoutée au présent document (identification, rôle, données, localisation, mécanisme de transfert, référence du DPA).
- [ ] Les **fiches de traitement concernées** du [registre](./processing-register.md) sont mises à jour : rubriques « sous-traitants », « destinataires » et « transferts hors UE ».
- [ ] La **politique de confidentialité** publiée est mise à jour — l'Art. 13(1)(e) impose de nommer les catégories de destinataires — et sa version est incrémentée.
- [ ] Si le sous-traitant implique le dépôt d'un **traceur non exempté** (analytics, capture de session, contenu embarqué), un **CMP conforme** est mis en place **avant** toute mise en production : refus aussi simple que l'acceptation, aucun dépôt avant choix, choix conservé 6 mois, retrait possible à tout moment.
- [ ] La [politique de conservation](./data-retention-policy.md) est complétée si le sous-traitant conserve des données pour son propre compte.

### Étape 5 — Suivi

- [ ] Vérification **annuelle** : validité du DPA, certification de transfert, liste des sous-traitants ultérieurs, absence d'incident déclaré.
- [ ] En cas de **rupture de la relation** : exercice de la clause de suppression (Art. 28(3)(g)) et **obtention d'une attestation de suppression**.

---

## 6. Historique des versions

| Version | Date | Modifications |
|---|---|---|
| 1.0 | 2026-09-11 | Création — Microsoft Azure et GitHub ; confirmation de l'absence d'analytics, d'emailing et d'outil de support tiers ; procédure d'ajout d'un sous-traitant. |

---

## 7. Documents liés

- [Index du dossier de conformité](./README.md)
- [Registre des activités de traitement](./processing-register.md)
- [Politique de conservation des données](./data-retention-policy.md)
- [Procédure de notification de violation de données](../security/breach-notification-procedure.md)
