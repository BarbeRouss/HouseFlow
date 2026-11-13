# Sous-traitants et destinataires

**Articles 28 et 44-49 du RGPD**

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | **Rouss Consulting SRL** (service HouseFlow) |
| **Contact vie privée** | `privacy@houseflow.cloud` |
| **Date** | 2026-09-11 |
| **Version** | 1.0 |
| **Revue** | annuelle, et à chaque ajout ou changement de sous-traitant |

> **Rappel.** L'article 28(1) interdit de faire appel à un sous-traitant qui ne présenterait pas de **garanties suffisantes**, et l'article 28(3) impose un **contrat écrit** couvrant huit obligations précises. Répondre « notre hébergeur est conforme au RGPD » n'exonère de rien : la conformité incombe au **responsable de traitement**, qui doit démontrer avoir choisi, encadré et vérifié chacun de ses sous-traitants.

---

## 1. Vue d'ensemble

| Sous-traitant | Rôle | Données traitées | Localisation | Mécanisme de transfert | DPA |
|---|---|---|---|---|---|
| [**Microsoft Azure**](#2-microsoft-azure) | Hébergement, base de données, réseau, journaux de plateforme | **Toutes** les données applicatives | West Europe (Pays-Bas) | Adéquation EU-US DPF + CCT (pour le support hors EEE) | Microsoft Products and Services DPA, **édition de novembre 2026** (voir § 2.5) |
| **GitHub** *(non sous-traitant — voir [§ 3](#3-github))* | Dépôt de code, CI/CD, registre d'images de conteneurs | **Aucune donnée personnelle d'utilisateur** | États-Unis / mondial | Sans objet | **Aucun contrat requis** (Art. 28 inapplicable) |

**Deux destinataires techniques, qualifiés le 2026-09-23 :**

- **OVH** — héberge les **zones DNS** du service. Un hébergeur DNS autoritaire ne traite **aucune donnée applicative** : il résout des noms, il ne voit ni les requêtes des utilisateurs ni leur contenu. **Non sous-traitant** au sens de l'Art. 28, mentionné ici par exigence d'exhaustivité.
- **Anthropic** — les agents Claude Code qui travaillent sur le dépôt (l'ancien workflow `claude-issue.yml`, supprimé le 2026-11-05, en était une forme) **lisent le dépôt**. Ce dépôt étant public et ne contenant **aucune donnée personnelle d'utilisateur** (le journal des demandes de droits est tenu **hors dépôt**, voir [§ 4 du journal](./rights-requests-log.md#4-journal-des-demandes)), il n'y a pas de traitement de données d'utilisateur pour le compte du responsable. **Non sous-traitant** en l'état. **Ce constat devient faux** si une donnée personnelle d'utilisateur est un jour versionnée : la règle de tenue hors dépôt est la mesure qui le garantit.

En dehors d'eux, HouseFlow n'utilise :

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
| **Qualification** | **Sous-traitant** au sens de l'Art. 4(8). L'absence d'accès de Microsoft au *contenu* de la base ne change rien : l'Art. 4(2) range explicitement la **conservation** parmi les traitements, et Azure ne se borne pas à conserver — il réplique, sauvegarde, applique les correctifs et collecte des journaux de plateforme. Le raisonnement inverse voudrait qu'aucun hébergeur ne soit jamais sous-traitant, ce qui viderait l'Art. 28 de son objet. Le chiffrement et l'absence d'accès réduisent le **risque**, jamais la qualification. |
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
| **Décision d'adéquation EU-US Data Privacy Framework** du 10 juillet 2023 | **Art. 45** | Le DPA (édition de novembre 2026) énonce que Microsoft est certifiée aux *EU-U.S. et Swiss-U.S. Data Privacy Frameworks* ainsi qu'à l'extension britannique, et s'engage à **notifier le client** si elle constate ne plus pouvoir assurer le niveau de protection exigé par les principes du cadre. Les transferts vers ses entités américaines certifiées peuvent donc se fonder sur l'adéquation. |
| **Clauses contractuelles types** de la Commission du 4 juin 2021 | **Art. 46(2)(c)** | Intégrées au DPA sous le nom de « *2021 Standard Contractual Clauses* », module **sous-traitant à sous-traitant**, entre *Microsoft Ireland Operations Limited* et *Microsoft Corporation*. Conservées comme **solution de repli** : l'invalidation successive du Safe Harbor (2015) puis du Privacy Shield (2020) commande de ne pas dépendre d'une seule décision d'adéquation. |
| **EU Data Boundary** | — | Pour les services couverts, le DPA engage Microsoft à stocker **et traiter** les données du client au sein de l'Union européenne et de l'AELE. Garantie de localisation, distincte des deux mécanismes de transfert ci-dessus. |

**Vérification périodique requise.** La certification DPF est renouvelée annuellement et peut être retirée. Sa validité doit être **vérifiée et consignée chaque année** sur la [Data Privacy Framework List](https://www.dataprivacyframework.gov/list) — la liste officielle fait foi, l'engagement contractuel de Microsoft n'en est que le reflet. Le site étant une application JavaScript, la consultation se fait **dans un navigateur** ; deux minutes, une fois par an.

| Date de vérification | Certification DPF de Microsoft | Vérificateur |
|---|---|---|
| 2026-09-26 | **Engagement contractuel constaté**, non confirmé sur la liste officielle : le DPA de mai 2026 énonce la certification aux cadres EU-U.S. et Swiss-U.S. et à l'extension britannique. La vérification sur la *Data Privacy Framework List* reste à faire dans un navigateur. | Session automatisée (relevé documentaire) |
| 2026-11-11 | **Engagement contractuel reconduit**, toujours non confirmé sur la liste officielle : l'édition de novembre 2026 reprend à l'identique la certification aux cadres EU-U.S. et Swiss-U.S. et à l'extension britannique, et l'engagement de notifier le client en cas d'impossibilité de respecter les principes du cadre. La vérification sur la *Data Privacy Framework List* reste à faire dans un navigateur. | Session automatisée (relevé documentaire) |

### 2.5 Contrat de sous-traitance (Art. 28(3))

Le **Microsoft Products and Services Data Protection Addendum (DPA)** est le document contractuel applicable. Le DPA le dit lui-même en introduction : « *The DPA is incorporated by reference into the Product Terms and other Microsoft agreements* » — il s'applique donc sans signature distincte, et prévaut en cas de conflit avec les autres conditions (« *the DPA Terms shall prevail* »). Les constats ci-dessous ont été relevés dans l'**édition de novembre 2026** (relevé du 2026-11-11, voir le tableau d'accountability). Ils avaient été établis sur l'édition de mai 2026 ; la comparaison des deux textes, paragraphe par paragraphe, ne fait apparaître que deux modifications, sans effet sur ce tableau (voir « Écarts entre les éditions » ci-dessous).

| Obligation Art. 28(3) | Couverture par le DPA Microsoft |
|---|---|
| **(a)** Traitement sur **instruction documentée** du responsable, y compris pour les transferts | Oui — Microsoft s'engage à ne traiter les *Customer Data* que sur instruction documentée du client. |
| **(b)** Engagement de **confidentialité** des personnes autorisées | Oui. |
| **(c)** Mise en œuvre des mesures de l'**Art. 32** | Oui — mesures techniques et organisationnelles décrites en annexe, qui doivent respecter « *the requirements set forth in ISO 27001, ISO 27002, and ISO 27018* » ; Microsoft s'interdit de retirer l'une de ces normes sans successeur. Les autres certifications (ISO 27017, SOC 1/2/3) relèvent du tableau des *Core Online Services* des Product Terms, non relu ici : elles ne sont pas un engagement du DPA. |
| **(d)** Conditions de recours à un **sous-traitant ultérieur** | Oui — contrat écrit imposant au sous-traitant ultérieur « *at least the level of data protection required of Microsoft by the DPA* », usage limité à la prestation confiée, et surveillance par Microsoft. Préavis d'ajout : **6 mois** avant tout accès aux données du client ; **30 jours** pour un sous-traitant servant une fonctionnalité d'intelligence artificielle, avec la faculté de désactiver son usage jusqu'à 6 mois après le préavis. |
| **(e)** **Assistance** pour répondre aux demandes d'exercice des droits | Oui. |
| **(f)** **Assistance** au respect des Art. 32 à 36, dont la **notification des violations** | Oui — termes GDPR du DPA : « *Microsoft shall notify Customer without undue delay after becoming aware of a Personal Data breach* » (Art. 33(2)), avec les informations exigées par l'Art. 33(3) dans la mesure où elles sont raisonnablement disponibles. La section « *Security Incident Notification* » ajoute « *promptly and without undue delay* », et l'annexe de sécurité précise : « *without undue delay and, in any event, within 72 hours* ». C'est cette notification qui déclenche notre propre horloge de 72 heures — voir la [procédure de notification de violation](../security/breach-notification-procedure.md). |
| **(g)** **Suppression ou restitution** des données en fin de prestation | Oui, et le délai est chiffré : les données restent **90 jours** dans un compte à fonctions réduites après l'expiration ou la résiliation de l'abonnement, pour permettre leur extraction, puis sont supprimées dans les **90 jours** suivants — soit **180 jours au plus**. Ce délai vise la fin de *notre* abonnement Azure ; il est **sans effet sur la suppression d'un compte utilisateur**, qui reste immédiate (voir la [politique de conservation § 4](./data-retention-policy.md#4-suppression-de-compte)). |
| **(h)** Mise à disposition des informations nécessaires aux **audits** | Oui — rapports d'audit indépendants mis à disposition. |

| Élément d'accountability | État |
|---|---|
| **Édition du DPA relevée** | **Novembre 2026** — le document porte « *Last updated November 1, 2026* » et « *These commitments are binding on Microsoft as of November 1, 2026* ». C'est l'édition en vigueur à la date de relevé ci-dessous ; les éditions antérieures (mai 2026, septembre 2025, avril 2025…) restent publiées à la même adresse. L'édition de mai 2026 (effet au 22/05/2026) est celle sur laquelle la version 1.0 de ce dossier reposait. |
| **Date de relevé** | **2026-11-11** — édition en vigueur constatée ce jour sur la page de référence, et document récupéré en anglais (`MicrosoftProductandServicesDPA(WW)(English)(November_2026)(CR).docx`). Relevé précédent : 2026-09-26 (édition de mai 2026). |
| **Contrat Microsoft auquel le DPA est rattaché** | *[à recopier par le responsable]* — type de contrat et date d'entrée en vigueur, établis selon la [méthode du § 2.5.1](#251-comment-établir-la-date-du-contrat). Seul élément du triplet qui ne se lit pas dans le document public. |
| **Copie archivée** | *[à faire par le responsable]* — archiver le fichier **hors du dépôt** (dépôt public), dans l'espace documentaire de Rouss Consulting SRL, puis indiquer ici **où** il est rangé — jamais son contenu. Archiver l'édition de **novembre 2026** et conserver celle de mai 2026 si elle l'a été. |
| **Référence publique** | [Microsoft Products and Services Data Protection Addendum](https://www.microsoft.com/licensing/docs/view/Microsoft-Products-and-Services-Data-Protection-Addendum-DPA) |
| **Sous-traitants ultérieurs** | [Liste publique des sous-traitants Microsoft](https://aka.ms/subprocessor) — l'ancien lien vers `/licensing/docs/view/Microsoft-Online-Services-Subprocessor-List` redirige désormais vers l'accueil du site. Édition de la liste identifiée au 2026-11-11 : **4 novembre 2026**. **Contenu non relu** : le PDF n'est servi que par une application JavaScript (aperçu en blob) et n'a pas pu être extrait par la session automatisée. Relecture des sous-traitants ultérieurs et de leurs lieux de traitement **à faire par le responsable** dans un navigateur ; aucun nouveau sous-traitant ni changement de localisation n'est donc affirmé ni écarté ici. |

**Écarts entre les éditions de mai 2026 et de novembre 2026** (comparaison du texte intégral des deux documents, relevé du 2026-11-11) :

| Section du DPA | Modification | Effet sur ce dossier |
|---|---|---|
| *Disclosure of Processed Data* | Ajout : « *If, in the course of providing a Microsoft Online Service, Microsoft becomes aware of apparent child sexual exploitation and abuse material, Microsoft may report such material to the National Center for Missing and Exploited Children or other authority.* » | Aucun sur les huit obligations de l'Art. 28(3) ni sur le lieu de traitement. À noter : c'est une faculté de signalement de Microsoft à une autorité, dont le NCMEC (États-Unis), qui s'ajoute aux cas de communication déjà prévus (« *as Customer directs; as described in this DPA; or as required by law* »). **Signalé à l'éditeur** pour qu'il en prenne acte (la portée pratique pour HouseFlow, selon les contenus que les utilisateurs peuvent y déposer, n'a pas été analysée ici). |
| *Previews* | Clarification : les termes « *Processing of Personal Data; GDPR, Data Security, and HIPAA Business Associate* » ne s'appliquent pas aux préversions « *unless the Previews allow for processing of Personal Data, except as stated otherwise for that Preview* » (la rédaction de mai exceptait les préversions « *that allow Personal Data Processing* »). | Aucun effet constaté dans le dossier, qui ne repose sur aucune préversion (services listés au § 2.2). **À vérifier par l'éditeur** avant tout recours à une préversion. |

Sont **inchangés** entre les deux éditions : le préavis de **6 mois** d'ajout d'un sous-traitant ultérieur et de **30 jours** pour un sous-traitant servant une fonctionnalité d'intelligence artificielle (avec faculté de désactivation jusqu'à 6 mois après le préavis), la rétention **90 jours + 90 jours**, la notification de violation « *without undue delay* » (et « *within 72 hours* » à l'annexe de sécurité), la certification DPF, les CCT 2021, l'*EU Data Boundary*, ainsi que les huit obligations de l'Art. 28(3) reprises dans les termes GDPR. Le tableau ci-dessus confirme donc la couverture « Oui » des huit obligations sur l'édition de novembre 2026 ; une correction de citation a été apportée à (c) (normes ISO réellement citées par le DPA) et à (f) (emplacement et texte de la citation).

#### 2.5.1 Comment établir la date du contrat

> **Le DPA ne s'accepte pas et ne se signe pas séparément.** Il est incorporé **par référence** au contrat client Microsoft : il n'existe donc, dans le portail Azure, aucun écran affichant « version du DPA acceptée le … ». Chercher un tel écran est une impasse. Ce qui se démontre, et qui suffit à l'Art. 28(3), est un triplet : **l'édition du DPA archivée**, **la date à laquelle elle a été récupérée**, et **le contrat auquel elle se rattache**.

Pour ce dernier point, dans cet ordre — la première étape qui aboutit suffit, il n'est pas nécessaire de toutes les faire :

1. **Type de contrat.** Portail Azure → *Cost Management + Billing* → *Billing scopes* s'il y en a plusieurs → le compte de facturation → *Properties*. Le champ **Agreement type** vaut `Microsoft Online Services Program` (compte créé en ligne, dit aussi *pay-as-you-go*), `Microsoft Customer Agreement`, `Enterprise Agreement` ou `Microsoft Partner Agreement`.
2. **Si le type est `Microsoft Customer Agreement` ou `Enterprise Agreement`** : une entrée *Agreements* existe à côté de *Properties*, et elle porte la date d'entrée en vigueur ainsi que le PDF du contrat. C'est la source la plus directe.
3. **Si le type est `Microsoft Online Services Program`** : **cette entrée n'existe pas**, et c'est normal — un compte ouvert en ligne n'a pas de contrat négocié à télécharger. La date d'entrée en vigueur est alors celle de l'ouverture du compte, à établir par l'étape suivante.
4. **Première facture.** *Cost Management + Billing* → *Invoices* : la période de facturation la plus ancienne borne la date d'ouverture. Sur un compte resté dans le crédit gratuit, aucune facture n'existe ; passer à l'étape suivante.
5. **E-mail d'ouverture de compte Microsoft**, reçu à l'adresse du compte le jour de la création de l'abonnement. À défaut, la date de création du plus ancien groupe de ressources fait foi comme borne supérieure.
6. **En ligne de commande**, si l'extension `billing` est installée (`az extension add --name billing`) :

```bash
# Type de contrat du compte de facturation
az billing account list --query "[].{name:name, agreement:agreementType, type:accountType}" -o table

# Contrats et dates d'effet — MCA et EA uniquement, vide sur un compte pay-as-you-go
az billing agreement list --account-name "<name renvoyé ci-dessus>"   --query "[].{id:name, effective:effectiveDate, expiration:expirationDate, status:status}" -o table
```

7. **En dernier recours**, ouvrir une demande de support Microsoft (catégorie *Billing*) demandant la date d'entrée en vigueur du contrat. En attendant la réponse, consigner la **date la plus ancienne vérifiable** en indiquant sa source : une date bornée et sourcée vaut mieux qu'une case vide, et l'essentiel de la démonstration attendue est que le DPA **s'applique** et que le responsable **en détient une copie**.

**Vérification annuelle associée.** L'édition archivée se périme : Microsoft publie de nouvelles versions du DPA. La [revue annuelle](./README.md#63-revue-annuelle) compare la date d'effet de l'édition archivée à celle publiée, et archive la nouvelle si elle a changé — en conservant l'ancienne, qui documente l'état du contrat sur la période écoulée.

---

## 3. GitHub

### 3.1 Identification et rôle

| Élément | Valeur |
|---|---|
| **Entité** | GitHub, Inc. (filiale de Microsoft Corporation) |
| **Qualification** | **Non sous-traitant** (décision du 2026-09-23, justifiée au § 3.5). GitHub ne traite **aucune** donnée personnelle pour le compte de Rouss Consulting SRL. Pour les comptes des contributeurs, GitHub agit comme **responsable de traitement autonome** — ces personnes ont leur propre relation contractuelle avec GitHub, hors du périmètre de HouseFlow. |
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
| **Contrat de sous-traitance (Art. 28)** | **Aucun — et aucun n'est requis.** L'Art. 28 n'impose un contrat qu'entre un responsable et un **sous-traitant**, c'est-à-dire celui qui traite des données personnelles **pour le compte** du responsable. GitHub n'en traite aucune (voir § 3.5). Archiver un contrat de sous-traitance laisserait entendre une relation qui n'existe pas. Référence publique, à titre documentaire : [GitHub Data Protection Agreement](https://docs.github.com/site-policy/privacy-policies/github-data-protection-agreement). |

### 3.4 Mesures de sécurité applicables

| Mesure | État |
|---|---|
| Authentification OIDC vers Azure, sans secret de longue durée | Actif — `permissions: id-token: write` |
| Actions GitHub épinglées par SHA de commit | Actif — protection contre la compromission d'une action tierce |
| Images de base Docker épinglées par empreinte SHA-256 | Actif |
| Permissions minimales des workflows | Actif — `permissions: contents: read` par défaut |
| Analyse de secrets et alertes de vulnérabilité | **À activer** — voir les [points ouverts du registre](./processing-register.md#5-points-ouverts) |


### 3.5 Pourquoi GitHub n'est pas sous-traitant, et ce qui le maintient

L'article 28 n'impose un contrat qu'entre un responsable et un **sous-traitant** : celui qui traite
des données personnelles **pour le compte** du responsable. Cette condition n'est pas remplie ici, et
le constat a été vérifié point par point le **2026-09-23** :

| Ce que GitHub détient | Donnée personnelle d'utilisateur ? |
|---|---|
| Code source, spécifications, présent dossier de conformité | Non |
| **Journal des demandes d'exercice de droits** | Non — il est tenu **hors dépôt** ([voir § 4 du journal](./rights-requests-log.md#4-journal-des-demandes)), précisément pour cette raison |
| Artefacts de CI | Non — binaires de compilation et résultats de tests ; les tests n'utilisent que des adresses générées (`@houseflow.test`) |
| Dump de production | Non — le job `dbtools` s'exécute dans le *Container Apps Environment* d'Azure, **seul chemin réseau** vers le serveur PostgreSQL privé ; le runner GitHub ne l'a pas |
| Adresse e-mail du mainteneur (`appsettings.json`, `prod.tfvars`, tests) | C'est **sa propre** donnée, dont il dispose librement en tant que personne concernée |
| Identités des contributeurs | Relation directe contributeur ↔ GitHub, GitHub y est responsable autonome |

**Conséquence.** Aucun contrat de sous-traitance n'est requis, et il serait trompeur d'en archiver un :
cela laisserait entendre une relation qui n'existe pas.

**Ce qui maintient ce constat vrai.** Trois règles, dont la violation ferait basculer GitHub au rang de
sous-traitant et rendrait un contrat nécessaire :

1. Le **journal des demandes de droits** ne se remplit jamais dans le dépôt. Une ligne inscrite dans un
   dépôt public y resterait par l'historique Git, même supprimée ensuite.
2. Aucun **export, capture ou extrait** de la base de production n'est versé dans le dépôt, joint à une
   issue ou à une pull request, ni déposé comme artefact de CI.
3. Aucune **donnée d'utilisateur** n'est recopiée dans une issue, y compris pour illustrer un incident :
   on y renvoie par identifiant de compte, jamais par adresse e-mail.

Ces règles valent aussi pour les agents Claude Code qui lisent le dépôt (c'était le rôle du workflow
`claude-issue.yml`, supprimé le 2026-11-05) : c'est le même périmètre de données, donc la même conclusion.

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
