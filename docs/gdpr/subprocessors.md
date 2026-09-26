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
| [**Microsoft Azure**](#2-microsoft-azure) | Hébergement, base de données, réseau, journaux de plateforme | **Toutes** les données applicatives | West Europe (Pays-Bas) | Adéquation EU-US DPF + CCT (pour le support hors EEE) | Microsoft Products and Services DPA — *version et date à recopier, voir § 2.5* |
| **GitHub** *(non sous-traitant — voir [§ 3](#3-github))* | Dépôt de code, CI/CD, registre d'images de conteneurs | **Aucune donnée personnelle d'utilisateur** | États-Unis / mondial | Sans objet | **Aucun contrat requis** (Art. 28 inapplicable) |

**Deux destinataires techniques, qualifiés le 2026-09-23 :**

- **OVH** — héberge les **zones DNS** du service. Un hébergeur DNS autoritaire ne traite **aucune donnée applicative** : il résout des noms, il ne voit ni les requêtes des utilisateurs ni leur contenu. **Non sous-traitant** au sens de l'Art. 28, mentionné ici par exigence d'exhaustivité.
- **Anthropic** — le workflow `.github/workflows/claude-issue.yml` déclenche une session d'agent qui **lit le dépôt**. Ce dépôt étant public et ne contenant **aucune donnée personnelle d'utilisateur** (le journal des demandes de droits est tenu **hors dépôt**, voir [§ 4 du journal](./rights-requests-log.md#4-journal-des-demandes)), il n'y a pas de traitement de données d'utilisateur pour le compte du responsable. **Non sous-traitant** en l'état. **Ce constat devient faux** si une donnée personnelle d'utilisateur est un jour versionnée : la règle de tenue hors dépôt est la mesure qui le garantit.

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
| **Édition du DPA archivée** | *[à recopier]* — la date d'effet imprimée en tête du PDF téléchargé (chaque édition porte un mois et une année). |
| **Date de téléchargement** | *[à recopier]* — le jour où le PDF a été récupéré depuis la référence publique ci-dessous. |
| **Contrat Microsoft auquel le DPA est rattaché** | *[à recopier]* — type de contrat et date d'entrée en vigueur, établis selon la [méthode du § 2.5.1](#251-comment-établir-la-date-du-contrat). |
| **Copie archivée** | *[à faire]* — archiver le PDF **hors du dépôt** (dépôt public), dans l'espace documentaire de Rouss Consulting SRL. Indiquer ici où il est rangé, pas son contenu. |
| **Référence publique** | [Microsoft Products and Services Data Protection Addendum](https://www.microsoft.com/licensing/docs/view/Microsoft-Products-and-Services-Data-Protection-Addendum-DPA) |
| **Sous-traitants ultérieurs** | [Liste publique des sous-traitants Microsoft](https://www.microsoft.com/licensing/docs/view/Microsoft-Online-Services-Subprocessor-List) |

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

Ces règles valent aussi pour le workflow `.github/workflows/claude-issue.yml`, qui fait lire le dépôt à
une session d'agent : c'est le même périmètre de données, donc la même conclusion.

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
