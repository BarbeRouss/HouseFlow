# Registre des activités de traitement

**Article 30(1) du RGPD — Registre tenu par le responsable de traitement**

> Format inspiré du [modèle de registre simplifié de la CNIL](https://www.cnil.fr/fr/RGPD-le-registre-des-activites-de-traitement).
> Ce document est la **source de vérité** des finalités, bases légales et durées de conservation.
> La politique de confidentialité publiée sur `/privacy` en est dérivée : toute divergence est un défaut à corriger dans la politique, pas dans le registre.

---

## 1. Informations générales (Art. 30(1)(a))

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | **Rouss Consulting SRL**, société à responsabilité limitée de droit belge, éditrice du service HouseFlow |
| **Numéro d'entreprise (BCE)** | 0805.984.579 |
| **Numéro de TVA** | BE 0805 984 579 |
| **Adresse postale** | **Non publiée sur le service** — décision de l'éditeur : le contact des personnes concernées se fait par e-mail. Le siège social figure à la Banque-Carrefour des Entreprises. À réexaminer à l'ouverture de la vente. |
| **Contact vie privée** | `privacy@houseflow.cloud` (constante `GdprPolicy.PrivacyContactEmail`) |
| **Contact sécurité** | `security@houseflow.cloud` (voir [SECURITY.md](../../SECURITY.md)) |
| **Délégué à la protection des données (DPO)** | Aucun — désignation non obligatoire, voir [annexe A](#annexe-a--analyse-de-la-nécessité-de-désigner-un-dpo-art-37). Un **référent vie privée** est désigné : il répond à l'adresse `privacy@houseflow.cloud`. |
| **Représentant (Art. 27)** | Sans objet — le responsable est établi dans l'Union européenne. |
| **Responsable conjoint (Art. 26)** | Aucun. |
| **Autorité de contrôle chef de file (Art. 56)** | **Autorité de protection des données (APD/GBA), Belgique** — l'établissement principal du responsable, Rouss Consulting SRL, est situé en Belgique. Toute personne conserve le droit de saisir l'autorité de **son** État membre de résidence (Art. 77(1)). |
| **Date de création du registre** | 2026-09-11 |
| **Dernière mise à jour** | 2026-09-26 |
| **Version** | 1.1 |

---

## 2. Applicabilité de l'exemption de l'article 30(5)

L'article 30(5) dispense de registre les organisations de **moins de 250 salariés**, **sauf** si le traitement :

1. est susceptible de comporter **un risque** pour les droits et libertés des personnes ; **ou**
2. **n'est pas occasionnel** ; **ou**
3. porte sur des **catégories particulières** de données (Art. 9) ou sur des données relatives aux condamnations (Art. 10).

Ces conditions sont **alternatives** : il suffit qu'une seule soit remplie pour que le registre redevienne obligatoire.

**Conclusion : l'exemption ne s'applique pas à HouseFlow.** Le traitement des comptes utilisateurs et des données de maintenance d'un service SaaS est **permanent, systématique et structurel** — il constitue l'objet même du service. Il n'est donc en aucun cas « occasionnel » au sens de l'article 30(5). La CNIL limite en pratique cette dérogation à des traitements ponctuels et sans risque, et a déjà sanctionné des organisations de moins de 250 salariés pour absence de registre sur ce fondement précis.

Le registre est par conséquent **obligatoire**, tenu sous forme écrite y compris électronique (Art. 30(3)), et mis à la disposition de l'autorité de contrôle sur demande (Art. 30(4)).

---

## 3. Fiches de traitement

### Traitement n° 1 — Gestion des comptes et authentification

| Rubrique (Art. 30(1)) | Contenu |
|---|---|
| **Référence** | TR-01 |
| **Finalité(s)** (b) | Créer et gérer le compte utilisateur ; authentifier l'utilisateur à chaque connexion ; maintenir la session ; appliquer les préférences d'affichage (thème, langue) ; conserver la preuve de l'acceptation des CGU et de la prise de connaissance de la politique de confidentialité. |
| **Base légale** | **Art. 6(1)(b) — exécution du contrat.** Les données d'identification sont strictement nécessaires pour fournir le service demandé par la personne. Aucun consentement au sens de l'Art. 7 n'est collecté : la case cochée à l'inscription est une **acceptation contractuelle des CGU**, doublée d'une **information Art. 13**, et non une autorisation de traiter. |
| **Catégories de personnes** (c) | Utilisateurs inscrits (personnes physiques majeures, ou d'au moins 15 ans en France / 13 ans en Belgique). |
| **Catégories de données** (c) | Table `Users` : `Id` (UUID), `Email` (255), `FirstName` (100), `LastName` (100), `PasswordHash` (BCrypt), `Theme` (20), `Language` (10), `CreatedAt`, `UpdatedAt`, `ConsentGivenAt`, `ConsentPolicyVersion`, `LastLoginAt` (date de dernière **activité** — connexion par mot de passe ou rafraîchissement de session, au plus une écriture par 24 h ; base de la procédure « comptes inactifs »), `ProcessingRestrictedAt` (limitation Art. 18), `IsAdmin` (20). |
| **Caractère obligatoire / facultatif** | **Obligatoires** : email, prénom, nom, mot de passe, acceptation des CGU. Le défaut de fourniture empêche la création du compte, et donc l'accès au service. **Facultatifs** : thème et langue (valeurs par défaut `system` / `fr`). |
| **Source des données** | Collectées **directement auprès de la personne** (formulaire d'inscription). |
| **Destinataires internes** (d) | L'utilisateur lui-même ; l'éditeur, pour l'exploitation et le support, dans la limite du besoin d'en connaître. Les **prénom, nom et adresse e-mail** sont visibles de **tous** les autres membres des maisons partagées, locataires compris : la liste des membres (`GET /houses/{id}/members`) est ouverte aux quatre rôles et renvoie l'e-mail. Divulgation nécessaire pour identifier sans ambiguïté avec qui une maison est partagée, et bornée aux membres de cette maison. |
| **Destinataires externes** (d) | Aucun tiers destinataire. |
| **Sous-traitants** | Microsoft Azure (hébergement, base de données) — voir [subprocessors.md](./subprocessors.md). |
| **Transferts hors UE** (e) | **Aucun transfert volontaire.** Hébergement Azure **West Europe** (Pays-Bas). Les accès support Microsoft depuis un pays tiers sont couverts par le DPA Microsoft, **édition de mai 2026** (« *2021 Standard Contractual Clauses* », module sous-traitant à sous-traitant Microsoft Ireland → Microsoft Corporation, + certification EU-US Data Privacy Framework du 10/07/2023). Voir [sous-traitants § 2.4](./subprocessors.md#24-transferts-hors-eee). |
| **Durée de conservation** (f) | **Durée de vie du compte.** Suppression **immédiate et définitive** à la demande de l'utilisateur (pas de période de grâce). Comptes inactifs : **3 ans** sans connexion → suppression après préavis. |
| **Mécanisme technique de purge** | Suppression de compte : `DELETE /api/v1/users/me` (suppression synchrone en base). Comptes inactifs : **procédure manuelle documentée** (requête SQL d'identification, puis préavis par email), décrite dans [data-retention-policy.md](./data-retention-policy.md). |
| **Mesures de sécurité** (g) | Hachage BCrypt du mot de passe (jamais réversible) ; politique de mot de passe de 8 caractères minimum avec minuscule, majuscule, chiffre et caractère spécial ; JWT d'accès de 15 minutes ; limitation à 5 requêtes/minute/IP sur les routes d'authentification en production ; TLS/HTTPS exclusif avec HSTS ; index unique sur `Email` ; `PasswordHash` exclu de la piste d'audit. Voir [annexe C](#annexe-c--description-générale-des-mesures-de-sécurité-art-321). |
| **Référence code** | `src/HouseFlow.API/Controllers/AuthController.cs`, `src/HouseFlow.API/Controllers/UsersController.cs`, `src/HouseFlow.Application/Services/AuthService.cs`, `src/HouseFlow.Application/Common/GdprPolicy.cs`, `src/HouseFlow.Core/Entities/User.cs`. |

---

### Traitement n° 2 — Suivi de maintenance immobilière

| Rubrique (Art. 30(1)) | Contenu |
|---|---|
| **Référence** | TR-02 |
| **Finalité(s)** (b) | Permettre à l'utilisateur d'enregistrer ses biens immobiliers, ses équipements et les types d'entretien associés ; calculer les échéances de maintenance ; historiser les interventions réalisées, leur coût et le prestataire intervenu. |
| **Base légale** | **Art. 6(1)(b) — exécution du contrat.** Ces données constituent l'objet même du service souscrit. |
| **Catégories de personnes** (c) | Utilisateurs propriétaires de maisons ; collaborateurs et locataires membres d'une maison partagée ; **prestataires de maintenance personnes physiques** cités dans le champ `Provider` d'une intervention (tiers, données collectées indirectement). |
| **Catégories de données** (c) | Table `Houses` : `Name`, `Address`, `ZipCode`, `City`, `Country`, `UserId` (propriétaire). Table `Devices` : `Name`, `Type`, `Brand`, `Model`, `InstallDate`. Table `MaintenanceTypes` : `Name`, `Periodicity`, `CustomDays`. Table `MaintenanceInstances` : `Date`, `Cost` (decimal 18,2), `Provider` (200), `Notes` (champ libre, 2000 caractères). Horodatages `CreatedAt` / `UpdatedAt` sur chaque table. |
| **Caractère obligatoire / facultatif** | **Obligatoires** : nom de la maison, nom et type de l'équipement, nom et périodicité du type d'entretien, date de l'intervention. **Facultatifs** : adresse, code postal, ville, pays, marque, modèle, date d'installation, coût, prestataire, notes. Le défaut de fourniture d'un champ facultatif n'a d'autre conséquence qu'un suivi moins riche. |
| **Source des données** | Saisies **directement par l'utilisateur** ou par un membre autorisé de la maison. Les données relatives à un **prestataire personne physique** sont collectées **indirectement** (l'utilisateur les saisit). |
| **Destinataires internes** (d) | Les membres de la maison concernée, selon leur rôle : `Owner`, `CollaboratorRW`, `CollaboratorRO`, `Tenant`. Le propriétaire et les deux rôles de collaborateur voient toujours les coûts. Pour un **locataire**, `CanViewCosts` (**faux par défaut**) masque à la fois le champ `Cost` **et le champ `Provider`** — le nom du prestataire — et le total dépensé lui est renvoyé à zéro (`MaintenanceService`, `DeviceService`, via `IHouseMemberService.ShouldHideCosts`). Un locataire peut enregistrer un entretien si `CanLogMaintenance` (**vrai par défaut**), sans pouvoir modifier ni supprimer un enregistrement existant. |
| **Destinataires externes** (d) | Aucun. |
| **Sous-traitants** | Microsoft Azure. |
| **Transferts hors UE** (e) | Aucun (Azure West Europe). |
| **Durée de conservation** (f) | **Durée de vie du compte / de la maison.** À la suppression du compte propriétaire : transfert de propriété au collaborateur non-locataire le plus ancien (`CollaboratorRW` prioritaire sur `CollaboratorRO`) s'il en existe un, **sinon suppression complète** de la maison et de tout son contenu. |
| **Mécanisme technique de purge** | Suppression en cascade PostgreSQL (`OnDelete(DeleteBehavior.Cascade)` de `House` vers `Devices`, `MaintenanceTypes`, `MaintenanceInstances`, `HouseMembers`, `Invitations`), déclenchée par `DELETE /api/v1/users/me` ou par la suppression d'une maison. |
| **Information des prestataires (Art. 14)** | L'éditeur **ne dispose d'aucune coordonnée** permettant de contacter un prestataire nommé dans le champ `Provider` : seule une chaîne de caractères libre est stockée, sans adresse ni email. L'information individuelle exigerait un **effort disproportionné** au sens de l'**Art. 14(5)(b)**. Mesure compensatoire retenue, conformément à cette disposition : une **information générale publique** figure dans la politique de confidentialité (section « Données de tiers »), et un libellé d'aide sous le champ `Provider` invite à ne saisir qu'une raison sociale ou un nom d'entreprise et à s'abstenir de toute donnée sensible. |
| **Champs libres** | Le champ `Notes` est un champ libre. Un libellé d'aide dissuade explicitement la saisie de données relevant de l'Art. 9 (santé d'un occupant, situation personnelle d'un prestataire). Aucune donnée sensible n'est collectée de manière intentionnelle ou structurée. |
| **Mesures de sécurité** (g) | Contrôle d'accès serveur **par ressource** (anti-IDOR) : tout accès à une maison, un équipement ou une intervention vérifie l'appartenance de l'utilisateur à la maison ; restriction des coûts par la permission `CanViewCosts` ; DTO explicites en sortie d'API (jamais d'entité de domaine brute) ; chiffrement au repos Azure PostgreSQL ; réseau privé (VNet, accès public désactivé). |
| **Référence code** | `src/HouseFlow.Application/Services/HouseService.cs`, `DeviceService.cs`, `MaintenanceService.cs`, `MaintenanceCalculatorService.cs`, `src/HouseFlow.Core/Entities/{House,Device,MaintenanceType,MaintenanceInstance}.cs`. |

---

### Traitement n° 3 — Collaboration et invitations

| Rubrique (Art. 30(1)) | Contenu |
|---|---|
| **Référence** | TR-03 |
| **Finalité(s)** (b) | Permettre à un utilisateur de partager une maison avec d'autres personnes ; gérer les rôles et permissions des membres ; émettre, accepter, révoquer et faire expirer des invitations de partage. |
| **Base légale** | **Adhésions (`HouseMembers`) : Art. 6(1)(b) — exécution du contrat** à l'égard de chaque membre (le partage est une fonctionnalité du service souscrit). **Émission d'une invitation : Art. 6(1)(f) — intérêt légitime** de l'utilisateur invitant et de l'éditeur à permettre le partage demandé et à le sécuriser par un jeton à durée limitée. Mise en balance documentée dans le [LIA](./legitimate-interest-assessment.md). |
| **Intérêts légitimes poursuivis (Art. 13(1)(d))** | Rendre possible la fonction de partage explicitement demandée par l'utilisateur ; garantir qu'un accès à une maison ne puisse être obtenu que par un jeton révocable et à durée limitée. |
| **Catégories de personnes** (c) | Utilisateurs invitants ; personnes destinataires d'un lien d'invitation ; membres d'une maison partagée. |
| **Catégories de données** (c) | Table `Invitations` : `Token` (100, opaque, aléatoire, index unique), `Role`, `Status` (`Pending` / `Accepted` / `Expired` / `Revoked`), `ExpiresAt`, `CreatedAt`, `AcceptedAt`, `RevokedAt`, `HouseId`, `CreatedByUserId`, `AcceptedByUserId`. Table `HouseMembers` : `UserId`, `HouseId`, `Role`, `CanLogMaintenance`, `CanViewCosts`, `CreatedAt`, `UpdatedAt`. |
| **Minimisation — point notable** | **Aucune adresse email de la personne invitée n'est collectée ni stockée.** L'invitation prend la forme d'un **lien porteur d'un jeton opaque**, que l'utilisateur invitant transmet par le canal de son choix, hors du service. HouseFlow ne traite donc, à ce stade, **aucune donnée d'un tiers non inscrit** au titre des invitations, ce qui écarte l'obligation d'information de l'**Art. 14** pour ce traitement. *Toute évolution introduisant l'envoi d'invitations par email devra ajouter la collecte de l'email du tiers à la présente fiche et prévoir l'information Art. 14 dans le corps de l'email.* |
| **Caractère obligatoire / facultatif** | Le rôle attribué est obligatoire à la création de l'invitation. |
| **Source des données** | Utilisateur invitant (rôle, maison) ; personne invitée (son propre compte, lors de l'acceptation). |
| **Destinataires internes** (d) | Les membres de la maison concernée. La consultation d'un lien d'invitation révèle au porteur du jeton le **nom de la maison**, le **rôle proposé** et les **prénom et nom de l'invitant** — divulgation minimale, nécessaire à une acceptation éclairée du partage et bornée dans le temps par l'expiration du jeton. |
| **Destinataires externes** (d) | Aucun. |
| **Sous-traitants** | Microsoft Azure. |
| **Transferts hors UE** (e) | Aucun. |
| **Durée de conservation** (f) | Invitation **non acceptée, expirée ou révoquée** : supprimée **30 jours après sa date d'expiration**. Invitation acceptée : supprimée selon la même règle, l'adhésion effective étant matérialisée par la ligne `HouseMembers`. Adhésion (`HouseMembers`) : durée du partage ; supprimée au retrait du membre ou à la suppression de son compte. |
| **Mécanisme technique de purge** | Job Hangfire récurrent quotidien `DataRetentionJob` (règle `ExpiredInvitationRetentionDays`) : marque `Expired` les invitations `Pending` dont `ExpiresAt` est dépassée, puis supprime définitivement toute invitation non `Pending` dont `ExpiresAt` remonte à plus de 30 jours. |
| **Mesures de sécurité** (g) | Jeton cryptographiquement aléatoire, opaque, à usage unique et à durée de vie limitée ; index unique ; révocation possible à tout moment par l'invitant ; vérification du rôle à chaque opération sur les membres. |
| **Référence code** | `src/HouseFlow.Application/Services/HouseMemberService.cs`, `src/HouseFlow.Infrastructure/Jobs/DataRetentionJob.cs`, `src/HouseFlow.Core/Entities/{Invitation,HouseMember}.cs`, `src/HouseFlow.API/Controllers/MembersController.cs`. |

---

### Traitement n° 4 — Sécurité, journalisation et traçabilité

| Rubrique (Art. 30(1)) | Contenu |
|---|---|
| **Référence** | TR-04 |
| **Finalité(s)** (b) | Assurer la sécurité du service et l'intégrité des données ; tracer les créations, modifications et suppressions d'enregistrements ; maintenir les sessions authentifiées et permettre leur révocation ; détecter et investiguer les accès non autorisés, les abus et les violations de données ; constituer la preuve des opérations en cas de litige ou de contrôle. |
| **Base légale** | **Art. 6(1)(f) — intérêt légitime.** Le **considérant 49** du RGPD reconnaît expressément la sécurité des réseaux et de l'information comme un intérêt légitime. Le maintien de session repose accessoirement sur l'**Art. 6(1)(b)**. Mise en balance documentée dans le [LIA](./legitimate-interest-assessment.md). |
| **Intérêts légitimes poursuivis (Art. 13(1)(d))** | Prévention des accès frauduleux aux comptes et aux données de maintenance des utilisateurs ; capacité à reconstituer la chronologie d'un incident et à en notifier les victimes (Art. 33-34) ; protection contre le bourrage d'identifiants et l'abus d'API. |
| **Catégories de personnes** (c) | Tous les utilisateurs ; toute personne émettant une requête vers l'API (y compris non authentifiée, pour la limitation de débit). |
| **Catégories de données** (c) | Table `AuditLogs` : `EntityType`, `EntityId`, `Action`, `UserId`, `Username`, `Timestamp`, `OldValues` (JSON), `NewValues` (JSON), `ChangedProperties` (JSON), `IpAddress`, `UserAgent`, `AdditionalData`. Table `RefreshTokens` : `Token` (haché), `ExpiresAt`, `CreatedAt`, `CreatedByIp`, `RevokedAt`, `RevokedByIp`, `ReplacedByToken`, `ReasonRevoked`. Table `ApiKeys` : `Name`, `Prefix`, `KeyHash` (SHA-256), `Scope`, `CreatedAt`, `CreatedByIp`, `LastUsedAt`, `RevokedAt`. Compteurs de limitation de débit : adresse IP, en **mémoire volatile uniquement**, non persistée. |
| **Minimisation appliquée** | Les valeurs `PasswordHash`, `Token` (rafraîchissement) et `KeyHash` sont **exclues** de la piste d'audit : elles ne sont jamais recopiées dans `OldValues` / `NewValues`. Les journaux applicatifs Serilog ne contiennent **aucune donnée personnelle** depuis le 2026-09-11. |
| **Caractère obligatoire** | La journalisation est **inhérente au fonctionnement sécurisé** du service ; elle n'est pas paramétrable par l'utilisateur. Ce point est explicité dans la politique de confidentialité et fonde la réponse motivée aux oppositions (Art. 21). |
| **Source des données** | Générées **par le système** à partir de l'activité de la personne (données observées), non déclarées par elle. À ce titre elles relèvent du droit d'accès (Art. 15) mais **pas du droit à la portabilité** (Art. 20). |
| **Destinataires internes** (d) | L'éditeur, pour l'exploitation, la sécurité et l'investigation d'incidents, dans la limite du besoin d'en connaître. Aucun accès utilisateur direct à la table `AuditLogs` ; l'utilisateur obtient les entrées le concernant via son export de données. |
| **Destinataires externes** (d) | Aucun, sauf réquisition d'une autorité judiciaire ou communication à l'autorité de contrôle dans le cadre d'une notification de violation. |
| **Sous-traitants** | Microsoft Azure (base de données ; Log Analytics pour les journaux de plateforme). |
| **Transferts hors UE** (e) | Aucun. |
| **Durée de conservation** (f) | **Journaux d'audit : 1 an** sous forme identifiante, puis **anonymisation** (`UserId`, `Username`, `IpAddress`, `UserAgent`, `OldValues`, `NewValues`, `ChangedProperties` effacés) ; **purge définitive à 3 ans**. **Adresses IP** (audit, refresh tokens, clés API) : conservées **complètes 30 jours**, puis **tronquées**. **Refresh tokens** révoqués ou expirés : purgés **30 jours** après révocation ou expiration. **Clés API** révoquées : purgées **30 jours** après révocation. Journaux de plateforme Azure Container Apps / Log Analytics : **30 jours**. |
| **Mécanisme technique de purge** | Job Hangfire récurrent `DataRetentionJob`, exécuté **quotidiennement à 03:00 UTC**, paramétré par la section `DataRetention` de `appsettings.json`. Il applique, dans l'ordre : troncature des IP de plus de 30 jours (`IpAddressAnonymizer`), anonymisation des entrées d'audit de plus d'un an, suppression des entrées d'audit de plus de trois ans, suppression des refresh tokens et clés API révoqués ou expirés depuis plus de 30 jours, purge des entités soft-deleted, puis expiration et purge des invitations (30 jours). Une seule exécution à la fois (`DisableConcurrentExecution`). Chaque exécution est journalisée avec les volumes traités (preuve d'accountability, Art. 5(2)). |
| **Arbitrage documenté — décret n° 2021-1362** | Le décret du 20 octobre 2021, pris pour l'application du II de l'article 6 de la LCEN, impose aux **hébergeurs de contenus destinés au public** la conservation, pendant un an, des données d'identification des contributeurs. **HouseFlow ne publie aucun contenu au public** : c'est un service privé de gestion de maintenance, dont les données ne sont accessibles qu'aux membres d'une maison. **Décision (2026-09-11) : ne pas se prévaloir de ce décret** comme obligation légale. La durée d'un an est retenue au titre de l'**intérêt légitime de sécurité** (Art. 6(1)(f)), dans la fourchette de 6 mois à 1 an recommandée par la CNIL dans sa recommandation relative aux mesures de journalisation. |
| **Mesures de sécurité** (g) | Clés API hachées en SHA-256 (la clé en clair n'existe qu'au moment de sa création et n'est jamais restockée) ; refresh tokens stockés hachés, avec **rotation** à chaque rafraîchissement et révocation du jeton remplacé ; cookie `refreshToken` en `HttpOnly` + `Secure` + `SameSite=Lax` + `Path=/api/v1/auth` ; accès à la base restreint au réseau privé (VNet), authentification Entra ID sans mot de passe ; index sur `Timestamp` et `UserId` permettant purges et investigations. |
| **Référence code** | `src/HouseFlow.Infrastructure/Data/HouseFlowDbContext.cs` (`OnBeforeSaveChanges` / `OnAfterSaveChanges`), `src/HouseFlow.API/Middleware/AuditContextMiddleware.cs`, `src/HouseFlow.API/Middleware/SecurityHeadersMiddleware.cs`, `src/HouseFlow.Infrastructure/Jobs/DataRetentionJob.cs`, `src/HouseFlow.Application/Common/IpAddressAnonymizer.cs`, `src/HouseFlow.Application/Services/ApiKeyService.cs`, `src/HouseFlow.API/Program.cs` (limitation de débit). |

---

### Traitement n° 5 — Exercice des droits RGPD

| Rubrique (Art. 30(1)) | Contenu |
|---|---|
| **Référence** | TR-05 |
| **Finalité(s)** (b) | Permettre aux personnes concernées d'exercer leurs droits d'accès, de portabilité, de rectification, d'effacement, de limitation et d'opposition ; conserver la preuve du traitement de chaque demande (accountability). |
| **Base légale** | **Art. 6(1)(c) — obligation légale** : le traitement des demandes est imposé par les articles 12 à 22 du RGPD. La conservation du **journal des demandes** repose sur l'**Art. 6(1)(f)** (intérêt légitime à démontrer la conformité, Art. 5(2)). |
| **Catégories de personnes** (c) | Utilisateurs inscrits ; anciens utilisateurs ; toute personne adressant une demande à `privacy@houseflow.cloud`. |
| **Catégories de données** (c) | **En ligne** : aucune donnée supplémentaire n'est créée — l'export lit les données existantes et la suppression les efface. Une entrée d'audit `Action = "DataExport"` et une entrée `Action = "AccountDeleted"` (sans donnée identifiante) sont écrites. **Hors ligne** ([journal des demandes](./rights-requests-log.md)) : date de réception, nature du droit exercé, canal, identifiant de la personne, mode de vérification d'identité, décision, motif, date de réponse. |
| **Caractère obligatoire** | Les informations demandées à l'appui d'une demande sont limitées au strict nécessaire pour identifier la personne et traiter sa demande. **Aucune copie de pièce d'identité n'est réclamée de façon systématique** : un utilisateur authentifié dans l'application est réputé identifié (Art. 12(6)). |
| **Source des données** | La personne concernée. |
| **Destinataires internes** (d) | Le référent vie privée. |
| **Destinataires externes** (d) | L'autorité de contrôle, en cas de réclamation ou de contrôle. |
| **Sous-traitants** | Microsoft Azure (opérations réalisées en ligne). Le journal des demandes est tenu hors du dépôt Git, dans l'espace documentaire du responsable (le dépôt est public). |
| **Transferts hors UE** (e) | Aucun. |
| **Durée de conservation** (f) | **Journal des demandes : 3 ans** à compter de la réponse (durée de preuve de conformité). Les exports générés ne sont **pas conservés** côté serveur : ils sont produits à la volée et transmis dans la réponse HTTP authentifiée. |
| **Mécanisme technique** | `GET /api/v1/users/me/export?format=json\|csv` (1 export maximum par heure et par utilisateur) ; `PUT /api/v1/users/me` (rectification) ; `DELETE /api/v1/users/me` (effacement immédiat) ; pages `/privacy` et `/terms` du frontend. Le journal des demandes est mis à jour manuellement. |
| **Garanties sur l'export** | Aucun secret n'est exporté (ni `PasswordHash`, ni `Token`, ni `KeyHash`). Aucune donnée identifiant un tiers n'est exportée : pour une maison partagée, seuls le nom de la maison et le rôle de la personne apparaissent, jamais les nom, prénom ou email des autres membres (Art. 15(4) et 20(4)). |
| **Mesures de sécurité** (g) | Authentification obligatoire ; limitation de fréquence de l'export ; réponse transmise sur canal TLS ; traçabilité de chaque export dans la piste d'audit. |
| **Référence code** | `src/HouseFlow.API/Controllers/UsersController.cs`, services d'export et de suppression de compte dans `src/HouseFlow.Application/Services/`, `specs/openapi.yaml` (section *USER ACCOUNT & RGPD*). |

---

### Traitement n° 6 — Support et contact

| Rubrique (Art. 30(1)) | Contenu |
|---|---|
| **Référence** | TR-06 |
| **Finalité(s)** (b) | Répondre aux demandes d'assistance, aux questions relatives à la vie privée et aux signalements de vulnérabilité. |
| **Base légale** | **Art. 6(1)(b)** pour le support lié à l'exécution du contrat ; **Art. 6(1)(f)** pour les échanges avec des personnes non inscrites (intérêt légitime à répondre à une sollicitation). |
| **Catégories de personnes** (c) | Utilisateurs ; chercheurs en sécurité ; toute personne écrivant à l'une des adresses publiées. |
| **Catégories de données** (c) | Adresse email de l'expéditeur, contenu du message et pièces jointes éventuelles, horodatage de l'échange. |
| **Caractère obligatoire / facultatif** | L'adresse email est nécessaire pour répondre ; le contenu du message est libre. |
| **Source des données** | La personne elle-même. |
| **Destinataires internes** (d) | L'éditeur et le référent vie privée. |
| **Destinataires externes** (d) | Aucun. |
| **Sous-traitants** | Fournisseur de messagerie de l'éditeur. **Aucun outil de support tiers (ticketing, chat, base de connaissances) n'est utilisé à ce jour.** |
| **Transferts hors UE** (e) | Aucun transfert volontaire ; à réévaluer si le fournisseur de messagerie change — voir [subprocessors.md](./subprocessors.md). |
| **Durée de conservation** (f) | 1 an après la clôture de l'échange. Un échange constituant une demande d'exercice de droits est en outre consigné 3 ans au [journal des demandes](./rights-requests-log.md). |
| **Mécanisme technique de purge** | Purge manuelle de la boîte de messagerie, à la revue annuelle. |
| **Mesures de sécurité** (g) | Accès à la boîte protégé par authentification forte ; échanges chiffrés en transit. |
| **Référence** | `privacy@houseflow.cloud` (vie privée et droits), `security@houseflow.cloud` (vulnérabilités, voir [SECURITY.md](../../SECURITY.md)). |

---

### Traitement n° 7 — Environnements techniques (prévisualisations de pull request)

| Rubrique (Art. 30(1)) | Contenu |
|---|---|
| **Référence** | TR-07 |
| **Finalité(s)** (b) | Tester et valider les évolutions du service, et notamment les migrations de schéma, sur des données réalistes avant leur mise en production. |
| **Base légale** | **Art. 6(1)(f) — intérêt légitime** à disposer d'un environnement de recette représentatif, **strictement subordonné** à la pseudonymisation préalable et vérifiée de toute donnée réelle, à l'exception des comptes expressément préservés ci-dessous. |
| **Catégories de personnes** (c) | **Aucun utilisateur du service**, dont les données sont pseudonymisées avant de quitter la production. **Exception assumée** : les comptes listés dans `preserved_emails` (`instances/prod.tfvars`) traversent la chaîne **intacts**, avec leurs maisons et tout leur contenu. À ce jour il s'agit du **compte du mainteneur lui-même** et d'un **compte de démonstration** à identité fictive. Aucune donnée d'un **autre utilisateur** n'est exposée en clair. **Décision du 2026-09-23** : le prestataire et les notes d'entretien (`MaintenanceInstances.Provider` et `.Notes`) sont pseudonymisés pour **toutes** les maisons, y compris celles des comptes préservés — ces champs nomment couramment un **tiers personne physique** (un artisan), qui n'a pas choisi que ses données partent dans un environnement jetable, et aucune décision du mainteneur ne peut couvrir cela. `AuditLogs."UserId"` est conservé : c'est une clé pseudonyme, qui ne renvoie qu'à une ligne `Users` elle-même pseudonymisée, sauf pour les comptes préservés dont les données sont maintenues par choix de leur titulaire. Toute adjonction à cette liste d'un compte appartenant à une autre personne exigerait son information préalable et la mise à jour de la présente fiche. |
| **Catégories de données** (c) | Copie de la base de production dont `dbtools/pseudonymize.sql` remplace tout ce qui identifie une personne : e-mail (`user-<id>@pseudonymise.invalid`), prénom et nom génériques, `PasswordHash` remplacé par un condensat BCrypt valide qu'aucun mot de passe ne vérifie, jetons d'invitation remplacés, nom et adresse postale des maisons (`Address`, `ZipCode`, `City`) remplacés, prestataires et notes d'entretien remplacés. **Supprimés en totalité** : tous les jetons de rafraîchissement, y compris ceux des comptes préservés. **Supprimées sauf comptes préservés** : les clés d'API. **Vidés pour tout le monde** : utilisateur, IP, user-agent, valeurs avant/après et données annexes des journaux d'audit. Le schéma `hangfire` n'est **pas copié** : les arguments de tâches peuvent porter des données personnelles. |
| **Source des données** | Base de production, via une base de travail intermédiaire (`houseflow_dumpwork`) **qui ne quitte jamais l'environnement de production** et sur laquelle seule s'applique la pseudonymisation. |
| **Destinataires** (d) | L'éditeur et les contributeurs du projet. |
| **Sous-traitants** | Microsoft Azure ; GitHub (exécution de la CI, hébergement des images de conteneurs). |
| **Transferts hors UE** (e) | GitHub Actions et GitHub Container Registry peuvent s'exécuter hors EEE. **Aucune donnée applicative n'y transite** : le dump ne traverse jamais le runner GitHub, qui n'a aucun chemin réseau vers les serveurs PostgreSQL privés. Seuls le code source, les artefacts de compilation et les secrets de déploiement y sont traités. |
| **Durée de conservation** (f) | Base de travail : détruite à la fin du job de dump. Blob `db-dumps/latest.dump` : **écrasé à chaque exécution nocturne**, un seul exemplaire conservé. Bases de prévisualisation : détruites à la fermeture de la pull request, et au plus tard à l'échéance de l'environnement (`expires_at`, traitée par le reaper). |
| **Mécanisme technique** | Job `job-dbtools-dump` (cron 02:00 UTC, production) : `pg_dump` vers `houseflow_dumpwork`, puis `dbtools/pseudonymize.sql`, puis `dbtools/verify.sql` — **une seule violation détectée et rien n'est publié**. Job `job-dbtools-restore` (environnement de PR), en une transaction et une seule fois. Voir `dbtools/README.md`. |
| **Règle de gouvernance** | **Interdiction absolue de restaurer une copie de production non pseudonymisée dans un environnement non productif.** La vérification par `verify.sql` est bloquante et non contournable : elle contrôle le résultat contre les valeurs **attendues**, et non contre les valeurs d'origine. Le test `PseudonymizationTests` échoue si une colonne texte est ajoutée au modèle sans être classée comme pseudonymisée ou comme non personnelle : **toute nouvelle colonne susceptible de porter une donnée personnelle est donc arrêtée par la CI**. |
| **Mesures de sécurité** (g) | Cloisonnement par souscription : la souscription jetable ne contient qu'une identité **en lecture seule** sur le blob des dumps, et n'a ni chemin réseau ni administrateur vers la base de production. Authentification **exclusivement** par identité managée (jetons Entra) : aucun mot de passe ni clé de compte n'existe nulle part. Secrets propres à chaque environnement ; authentification OIDC sans secret de longue durée entre GitHub Actions et Azure. |
| **Référence code** | `dbtools/` (`pseudonymize.sql`, `verify.sql`, `README.md`), `infrastructure/terraform/environment/dbtools.tf`, `infrastructure/terraform/shared/rbac.tf`, `.github/workflows/pr-preview.yml`, `tests/HouseFlow.IntegrationTests/Pseudonymization/PseudonymizationTests.cs`. |

### Traitement n° 8 — Administration de la plateforme

| Rubrique (Art. 30(1)) | Contenu |
|---|---|
| **Référence** | TR-08 |
| **Finalité(s)** (b) | Exploiter le service : mesurer l'usage global, retrouver un compte pour traiter une demande ou un incident, et désigner les comptes administrateurs. |
| **Base légale** | **Art. 6(1)(f) — intérêt légitime** de l'éditeur à exploiter et maintenir son service, et **Art. 6(1)(c)** pour ce qui sert à répondre aux demandes d'exercice des droits. |
| **Catégories de personnes** (c) | **Tous les utilisateurs inscrits**, sans exception : l'administrateur voit l'ensemble des comptes. |
| **Catégories de données** (c) | Identité (`Email`, `FirstName`, `LastName`), `CreatedAt`, `IsAdmin`, `LastLoginAt`, et les compteurs agrégés de `/admin/stats`. La recherche s'effectue **sur l'adresse e-mail**. Aucune donnée de maison, d'appareil ou d'entretien n'est accessible par cette voie. |
| **Destinataires** (d) | Les seuls comptes portant `IsAdmin`. Le rôle ne voyage **que dans un JWT** : une clé d'API, même appartenant à un administrateur, n'atteint pas ces endpoints. |
| **Sous-traitants** | Microsoft Azure (hébergement). |
| **Transferts hors UE** (e) | Aucun. |
| **Durée de conservation** (f) | Aucune conservation propre : le back-office lit la base applicative, dont les durées sont celles de TR-01. Les consultations sont tracées par l'audit trail (TR-04). |
| **Mécanisme technique** | `AdminController` (`GET /admin/stats`, `GET /admin/users?search=`, `PUT /admin/users/{id}/admin`), `AdminService`. L'amorçage se fait par `Admin:BootstrapEmails` (`appsettings.json`), promu au démarrage de l'API. |
| **Règle de gouvernance** | Le nombre d'administrateurs est tenu au minimum. La comparaison des adresses d'amorçage est **insensible à la casse**, et l'unicité des adresses l'est également à l'inscription comme à la rectification : sans cela, une variante de casse d'une adresse d'amorçage permettrait de se faire promuvoir. |
| **Mesures de sécurité** (g) | Pagination bornée (20 par défaut, 100 au maximum) ; rôle porté par le seul JWT ; toute promotion ou révocation est inscrite à l'audit trail. |
| **Référence code** | `src/HouseFlow.API/Controllers/AdminController.cs`, `src/HouseFlow.Application/Services/AdminService.cs`, `src/HouseFlow.Application/Common/AdminBootstrap.cs`. |

---

## Annexe A — Analyse de la nécessité de désigner un DPO (Art. 37)

L'article 37(1) impose la désignation d'un délégué à la protection des données dans **trois cas limitativement énumérés**.

| Cas de l'Art. 37(1) | Applicable à HouseFlow ? | Analyse |
|---|---|---|
| **(a)** Le traitement est effectué par une **autorité publique** ou un **organisme public**, à l'exception des juridictions agissant dans l'exercice de leur fonction juridictionnelle. | **Non** | HouseFlow est une initiative privée éditée par un acteur privé. |
| **(b)** Les activités de base consistent en des opérations exigeant un **suivi régulier et systématique à grande échelle** des personnes concernées. | **Non** | Le service ne pratique aucun suivi comportemental : ni profilage, ni scoring, ni publicité ciblée, ni traçage inter-sites, ni géolocalisation. La journalisation d'audit est une mesure de sécurité interne, non un suivi des personnes au sens du considérant 24. Le volume d'utilisateurs ne relève par ailleurs pas de la « grande échelle » au sens des lignes directrices WP243. |
| **(c)** Les activités de base consistent en un traitement **à grande échelle** de catégories particulières de données (Art. 9) ou de données relatives à des **condamnations pénales** (Art. 10). | **Non** | Aucune donnée de l'article 9 (santé, opinions, biométrie, orientation sexuelle, convictions) ni de l'article 10 n'est collectée. Les seuls champs libres (`Notes`, `Provider`) font l'objet d'un libellé d'aide dissuadant la saisie de telles données et ne constituent pas un traitement structuré de catégories particulières. |

**Conclusion (décision du 2026-09-11).** Aucun des trois cas n'est rempli : **la désignation d'un DPO n'est pas obligatoire**. Conformément à la bonne pratique recommandée par la CNIL, un **référent vie privée** est néanmoins désigné au sein de l'éditeur ; il est joignable à `privacy@houseflow.cloud` et assume les missions pratiques de point de contact, de tenue du présent registre, de suivi du [journal des demandes](./rights-requests-log.md) et de pilotage de la [procédure de violation](../security/breach-notification-procedure.md).

Cette analyse doit être **réexaminée** si le service introduit du profilage, de la publicité ciblée, de la géolocalisation, ou toute collecte relevant de l'article 9.

---

## Annexe B — Analyse de la nécessité d'une AIPD (Art. 35)

### B.1 — Cas de l'article 35(3)

| Cas de l'Art. 35(3) | Applicable ? | Analyse |
|---|---|---|
| **(a)** Évaluation systématique et approfondie d'aspects personnels fondée sur un **traitement automatisé**, y compris le profilage, servant de base à des **décisions produisant des effets juridiques** ou affectant significativement la personne. | **Non** | Aucune décision automatisée n'est prise. Le calcul des échéances de maintenance porte sur des **équipements**, non sur des personnes, et ne produit aucun effet juridique. |
| **(b)** Traitement **à grande échelle** de catégories particulières (Art. 9) ou de données relatives aux condamnations (Art. 10). | **Non** | Aucune donnée des articles 9 ou 10 n'est traitée. |
| **(c)** **Surveillance systématique à grande échelle** d'une zone accessible au public. | **Non** | Sans objet : aucun dispositif de vidéosurveillance, de captation ou de géolocalisation. |

### B.2 — Critères des lignes directrices EDPB (WP248 rev.01)

Le WP248 retient neuf critères ; une AIPD est en principe requise lorsque **au moins deux** d'entre eux sont réunis.

| # | Critère WP248 | Rempli ? | Analyse |
|---|---|---|---|
| 1 | Évaluation ou notation (*scoring*, profilage) | **Non** | Aucun score, aucune segmentation, aucune prédiction portant sur une personne. |
| 2 | Décision automatisée produisant un effet juridique ou similaire | **Non** | Aucune décision automatisée au sens de l'Art. 22. |
| 3 | Surveillance systématique | **Non** | Les journaux d'audit tracent les opérations effectuées sur les données du compte ; ils ne surveillent pas le comportement des personnes et ne sont pas exploités à cette fin. |
| 4 | Données sensibles ou à caractère hautement personnel | **Non** | Ni données de l'Art. 9, ni données de paiement, ni documents d'identité. Le coût d'un entretien est une donnée de gestion de bien, non une donnée bancaire. Les champs libres font l'objet d'une mesure de prévention. |
| 5 | Traitement de données à grande échelle | **Non** | Base d'utilisateurs limitée, sans commune mesure avec les seuils du considérant 91 ; aucune couverture d'une part significative d'une population. |
| 6 | Croisement ou combinaison d'ensembles de données | **Non** | Aucune donnée n'est acquise auprès de tiers, aucun enrichissement, aucun recoupement avec une source externe. |
| 7 | Données concernant des personnes **vulnérables** | **Non** | Le service s'adresse à des adultes propriétaires ou occupants d'un logement. Aucun ciblage de mineurs, de patients ou de personnes en situation de dépendance. |
| 8 | Usage innovant, application de nouvelles technologies | **Non** | Architecture web classique (API REST, base relationnelle). Ni IA, ni biométrie, ni IoT, ni objet connecté. |
| 9 | Traitement empêchant les personnes d'exercer un droit ou de bénéficier d'un contrat ou d'un service | **Non** | Aucun refus, aucune exclusion, aucune restriction d'accès n'est fondée sur le traitement. |

**Résultat : 0 critère sur 9 rempli.**

**Conclusion (décision du 2026-09-11).** Aucun cas de l'article 35(3) n'est caractérisé, aucun critère du WP248 n'est rempli, et les traitements de HouseFlow ne figurent pas sur la liste des types d'opérations pour lesquelles la CNIL a rendu une AIPD obligatoire. **Une analyse d'impact relative à la protection des données n'est pas requise.**

Cette conclusion sera **réexaminée** en cas d'introduction : de fonctionnalités de géolocalisation des biens, d'analyse comportementale ou de recommandation personnalisée, de traitement de données de santé des occupants, d'intégration d'objets connectés remontant des données d'usage du logement, de traitement de moyens de paiement, ou de croissance portant la base d'utilisateurs à une échelle significative.

---

## Annexe C — Description générale des mesures de sécurité (Art. 32(1))

Description exigée par l'**Art. 30(1)(g)** — état réel de l'implémentation au 2026-09-11.

### C.1 — Confidentialité et contrôle d'accès

| Mesure | Mise en œuvre |
|---|---|
| Hachage des mots de passe | **BCrypt** avec sel intégré, jamais réversible. Le `PasswordHash` est exclu de la piste d'audit et de tout export. |
| Politique de mot de passe | **8 caractères minimum**, avec au moins une minuscule, une majuscule, un chiffre **et un caractère spécial** (4 catégories sur 4). Conforme à la recommandation CNIL de 2022 : celle-ci admet 8 caractères dès lors qu'un **mécanisme de limitation des tentatives** protège le compte, ce qui est le cas ici (5 requêtes/minute/IP sur les routes d'authentification). Sans ce mécanisme, 12 caractères seraient exigés. Appliquée côté client (HTML5) et côté serveur (contrat OpenAPI → annotations générées). |
| Jetons d'accès | **JWT signés HMAC-SHA256**, durée de vie **15 minutes**, validation stricte de l'émetteur, de l'audience, de la durée de vie et de la signature, `ClockSkew` réglé à zéro. Longueur minimale de clé imposée au démarrage (256 bits), sinon l'application refuse de démarrer. |
| Jetons de rafraîchissement | 64 octets d'aléa cryptographique, **stockés hachés** en base, **rotation systématique** à chaque rafraîchissement avec révocation du jeton remplacé (`ReplacedByToken`, `ReasonRevoked`), durée de vie **24 h** pour une session ordinaire et **365 jours glissants** si l'utilisateur coche « Se souvenir de moi », au plus **10 sessions** (familles de jetons) actives par utilisateur, la moins récemment utilisée étant évincée. |
| Cookie de session | `refreshToken` en **`HttpOnly`** (inaccessible au JavaScript), **`Secure`**, **`SameSite=Lax`**, **`Path=/api/v1/auth`**. Le jeton n'est jamais renvoyé dans le corps de la réponse. |
| Clés API | Préfixe `hf_` suivi d'un corps aléatoire ; seul le **hachage SHA-256** est stocké (`KeyHash`, 64 caractères) ; la clé en clair n'est affichée qu'une fois, à la création. Portée (`Scope`) appliquée par un filtre serveur (`ApiKeyScopeEnforcementFilter`). |
| Contrôle d'accès | Vérification **par ressource et côté serveur** de l'appartenance à la maison pour toute opération (protection anti-IDOR) ; rôles `Owner` / `CollaboratorRW` / `CollaboratorRO` / `Tenant` ; permission distincte `CanViewCosts` pour l'accès aux montants. |
| Exposition des données | Aucune entité de domaine n'est renvoyée par l'API : DTO explicites, pagination bornée (20 par défaut, 100 au maximum) sur la liste d'administration ; les listes applicatives ne sont pas encore paginées (point ouvert). |
| Accès administrateur à la base | **Authentification Entra ID sans mot de passe** : `password_auth_enabled = false` sur le serveur PostgreSQL ; l'API s'authentifie par identité managée, les administrateurs par leur compte Entra nominatif. Accès humain uniquement via un **bastion SSH** déployé en Container App, lui-même placé dans le VNet. **Authentification multifacteur en place sur le tenant Entra ID** (déclaration de l'éditeur, 2026-09-26) : elle protège donc les accès d'administration à l'hébergement et à la base. Elle **n'est pas** activée sur le compte GitHub, qui ne détient aucune donnée personnelle d'utilisateur (voir [sous-traitants § 3](./subprocessors.md)). Les pages légales **n'annoncent pas** la MFA aux utilisateurs — décision de l'éditeur du 2026-09-26 : ne rien promettre qui ne soit démontrable depuis le dépôt. |

### C.2 — Intégrité et protection des échanges

| Mesure | Mise en œuvre |
|---|---|
| Chiffrement en transit | **HTTPS exclusif**, redirection HTTP → HTTPS, **HSTS** `max-age=31536000; includeSubDomains; preload`. |
| En-têtes de sécurité | `SecurityHeadersMiddleware` : `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `X-XSS-Protection: 1; mode=block`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy: geolocation=(), microphone=(), camera=(), payment=()`, **CSP** (`default-src 'none'; frame-ancestors 'none'` sur l'API, qui ne sert que du JSON). Le frontend, servi en statique, ne pose pas encore de CSP : point ouvert du § 5. |
| CORS | Origines **restreintes** par la variable `CORS__ORIGINS`, méthodes limitées à `GET`, `POST`, `PUT`, `DELETE`, en-têtes limités à `Authorization` et `Content-Type`. |
| Limitation de débit | Active en **production et préproduction** : 5 requêtes/minute/IP sur les routes d'authentification (anti-bourrage d'identifiants), 100/minute/IP sur l'API, 200/minute/IP en garde-fou global ; réponse HTTP 429 accompagnée d'un `retryAfter`. |
| Validation des entrées | `DataAnnotations` sur tous les DTO (format, longueur, expressions régulières, bornes) ; requêtes paramétrées via Entity Framework (protection contre l'injection SQL). |
| Messages d'erreur | Génériques à l'authentification, afin d'empêcher l'énumération des comptes. |

### C.3 — Cloisonnement réseau et hébergement

| Mesure | Mise en œuvre |
|---|---|
| Région d'hébergement | **Azure West Europe** (Pays-Bas) — `var.location = "westeurope"`. |
| Base de données | **Azure Database for PostgreSQL Flexible Server 16**, `public_network_access_enabled = false`, intégrée au VNet par sous-réseau délégué (`snet-db`, /28) et zone DNS privée. Chiffrement au repos activé par la plateforme. |
| Application | Azure Container Apps, sous-réseau délégué dédié (`snet-apps`, /23). |
| Secrets | **Absents du dépôt** ; injectés par variables d'environnement et secrets de plateforme. Authentification **OIDC sans secret de longue durée** entre GitHub Actions et Azure (`id-token: write`). |
| Images de conteneurs | Images de base **épinglées par empreinte SHA-256** (`mcr.microsoft.com/dotnet/aspnet:10.0@sha256:…`). Actions GitHub **épinglées par SHA de commit**. |

### C.4 — Disponibilité et résilience

| Mesure | Mise en œuvre |
|---|---|
| Sauvegardes | Azure PostgreSQL Flexible Server, **restauration dans le temps (PITR)** — `backup_retention_days = 7`, `geo_redundant_backup_enabled = false` (les sauvegardes restent dans la région West Europe, donc dans l'EEE). |
| Test de restauration | **Obligation Art. 32(1)(c) : à planifier et à dater au moins une fois par an.** Dernier test réalisé : *[à compléter]*. |
| Sondes de santé | `/health` (avec vérification de la base) et `/alive`. |
| Journaux de plateforme | Azure Container Apps → Log Analytics, rétention **30 jours** (`retention_in_days = 30`). |

### C.5 — Traçabilité

| Mesure | Mise en œuvre |
|---|---|
| Piste d'audit applicative | Journalisation automatique, dans `SaveChangesAsync`, de toute création, modification ou suppression d'entité : type, identifiant, action, auteur, horodatage, valeurs avant/après, propriétés modifiées, IP, agent utilisateur. Les secrets (`PasswordHash`, `Token`, `KeyHash`) en sont **exclus**. |
| Contexte d'audit | `AuditContextMiddleware` renseigne l'identité, l'IP et l'agent utilisateur à chaque requête. |
| Journaux applicatifs | Serilog vers la sortie standard, collectée par Azure Container Apps. **Aucune donnée personnelle n'y figure depuis le 2026-09-11.** |
| Purge et anonymisation | `DataRetentionJob` (quotidien, 03:00 UTC) — voir [data-retention-policy.md](./data-retention-policy.md). |

### C.6 — Mesures organisationnelles et évaluation régulière (Art. 32(1)(d))

| Mesure | État |
|---|---|
| Revue de code systématique | Toute modification passe par une pull request ; la CI exécute les tests unitaires, d'intégration et E2E avant fusion. |
| Revue des dépendances | `dotnet list package --vulnerable` et `npm audit` — **à intégrer en CI** (voir [points ouverts](#5-points-ouverts)). |
| Évaluation périodique de la sécurité | **Revue annuelle documentée à planifier**, comprenant : revue du présent registre, test de restauration de sauvegarde, exercice de simulation de violation. |
| Confidentialité des intervenants | Toute personne ayant accès aux données de production agit **sur instruction documentée du responsable** et est tenue à une obligation de confidentialité écrite (Art. 28(3)(b), 32(4)). |
| Séparation des environnements | Production, préproduction et prévisualisations strictement cloisonnées ; anonymisation obligatoire avant toute copie (TR-07). |

---

## 4. Procédure de mise à jour du registre

Le registre doit refléter en permanence l'état réel des traitements. **La date de dernière mise à jour est le premier élément examiné lors d'un contrôle.**

### Événements déclencheurs — mise à jour obligatoire

Toute évolution relevant de l'un des cas suivants **exige** la mise à jour du registre **avant la mise en production** :

1. **Nouveau traitement** ou nouvelle finalité pour un traitement existant.
2. **Nouvelle catégorie de données** : ajout d'une colonne, d'une table ou d'un champ contenant une donnée à caractère personnel.
3. **Nouveau sous-traitant** ou nouveau destinataire (y compris un nouvel outil SaaS : emailing, analytics, supervision, support).
4. **Changement de base légale** ou de durée de conservation.
5. **Nouveau transfert hors EEE** ou changement de région d'hébergement.
6. **Modification substantielle des mesures de sécurité** (annexe C).
7. **Modification du responsable de traitement**, de ses coordonnées ou du référent vie privée.

### Definition of done d'une fonctionnalité touchant des données personnelles

Une fonctionnalité manipulant des données à caractère personnel n'est **pas terminée** tant que les cinq points suivants ne sont pas satisfaits :

- [ ] La fiche de traitement concernée de `docs/gdpr/processing-register.md` est créée ou mise à jour (finalité, base légale, catégories de données, durée, destinataires, mesures).
- [ ] La **date de dernière mise à jour** et la **version** de l'en-tête du registre sont incrémentées.
- [ ] La [politique de rétention](./data-retention-policy.md) est alignée, et la purge correspondante est **implémentée et testée** (pas seulement écrite).
- [ ] La **politique de confidentialité** publiée (`/privacy`) est mise à jour et sa version incrémentée si l'information des personnes en est affectée.
- [ ] Si la base retenue est l'**intérêt légitime**, une section est ajoutée au [LIA](./legitimate-interest-assessment.md) ; si un **sous-traitant** est ajouté, la fiche correspondante est créée dans [subprocessors.md](./subprocessors.md) et son DPA archivé.

### Revue périodique

Revue complète **annuelle** du registre par le référent vie privée, même en l'absence d'évolution fonctionnelle : vérification de la concordance entre le registre, le schéma de base de données réel et les durées effectivement appliquées en base. Cette revue est consignée dans l'historique ci-dessous.

---

## 5. Points ouverts

| # | Point | Responsable | Échéance |
|---|---|---|---|
| 1 | ~~Compléter l'**adresse postale** du responsable de traitement~~ — **décision du 2026-09-23 : non publiée**, le contact se faisant par e-mail. À réexaminer à l'ouverture de la vente, le commerce électronique imposant alors une adresse géographique | — | clos |
| 2 | ~~Arrêter l'**autorité de contrôle chef de file**~~ — **décision du 2026-09-23 : APD/GBA (Belgique)**, établissement principal du responsable | — | clos |
| 3 | Archiver le **DPA Microsoft** hors dépôt et consigner l'emplacement — l'édition (**mai 2026**), sa date d'effet (22/05/2026) et la couverture des huit obligations de l'Art. 28(3) sont relevées au [§ 2.5](./subprocessors.md#25-contrat-de-sous-traitance-art-283) depuis le 2026-09-26 ; restent la copie archivée et le contrat de rattachement | Éditeur | — |
| 4 | Réaliser et **dater un test de restauration** de sauvegarde (Art. 32(1)(c)) | Éditeur | annuel |
| 5 | Intégrer `dotnet list package --vulnerable` et `npm audit` en CI ; activer Dependabot | Éditeur | — |
| 6 | Réaliser le premier **exercice de simulation de violation** | Référent vie privée | annuel |
| 7 | ~~Pseudonymiser aussi les maisons préservées sur les champs pouvant nommer un tiers~~ — **fait le 2026-09-23** : `Provider` et `Notes` sont traités pour toutes les maisons, et `verify.sql` le contrôle sur l'ensemble de la table | — | clos |
| 8 | Poser une **CSP** sur le frontend (aucune aujourd'hui, l'API en a une) | Éditeur | — |
| 9 | ~~Qualifier OVH et Anthropic~~ — **fait le 2026-09-23** : ni l'un ni l'autre ne traite de donnée personnelle d'utilisateur (OVH ne voit que des enregistrements DNS ; le dépôt lu par l'agent ne contient aucune donnée d'utilisateur, le journal des demandes étant tenu hors dépôt). Voir [sous-traitants § 1](./subprocessors.md) | — | clos |
| 10 | ~~Décider si les données réelles du mainteneur alimentent les environnements de prévisualisation~~ — **décision du 2026-09-23 : oui, elles sont conservées.** Le mainteneur est à la fois responsable et personne concernée pour ces données. Les seuls champs pouvant nommer un tiers sont pseudonymisés (voir n° 7). Toute adjonction à `preserved_emails` d'un compte appartenant à une **autre** personne exigerait son information préalable | — | clos |

---

## 6. Historique des versions

| Version | Date | Auteur | Modifications |
|---|---|---|---|
| 1.0 | 2026-09-11 | Référent vie privée | Création du registre — 7 fiches de traitement, analyses DPO et AIPD, description générale des mesures de sécurité, procédure de mise à jour. |
| 1.1 | 2026-09-26 | Référent vie privée | Relecture juridique des pages légales. Trois corrections de l'information donnée aux personnes, sans changement de traitement : identification légale du responsable complétée (BCE, TVA) sur les quatre pages ; base légale de la preuve d'acceptation des conditions corrigée en Art. 6(1)(b), l'Art. 5(2) n'étant pas une base légale ; destinataires précisés — un collaborateur en lecture et écriture peut inviter un locataire sans le propriétaire. Le responsable nommé dans le volet métadonnées de l'export Art. 15 était un pseudonyme : il provient désormais de `GdprPolicy.ControllerName`. Deux affirmations du registre corrigées au passage, contredites par le code : la liste des membres d'une maison expose l'**adresse e-mail** de chaque membre à tous les rôles, locataire compris (la fiche 1 affirmait l'inverse) ; et le drapeau `CanViewCosts`, propre aux locataires, masque le **prestataire** autant que le coût. Version de la politique portée à 2026-09-26. Reste de la relecture appliqué : exemption de cookies rattachée à la directive 2002/58/CE et à l'art. 129 de la loi belge du 13 juin 2005 ; journaux d'audit décrits avec l'adresse e-mail ; jetons de session ramenés à la seule base 6(1)(f), conformément au LIA ; clauses de responsabilité, de modification et de juridiction des CGU alignées sur le droit belge de la consommation ; critère d'inactivité corrigé à la racine : `AuthService` écrit désormais `LastLoginAt` aussi sur le chemin de rafraîchissement (au plus une fois par 24 h, hors journal d'audit), de sorte qu'un utilisateur actif en session « Se souvenir de moi » n'est plus qualifié d'inactif. DPA Microsoft relevé dans le document lui-même (édition de mai 2026, effet au 22/05/2026) : couverture des huit obligations de l'Art. 28(3) précisée avec les délais réels — préavis de 6 mois pour un nouveau sous-traitant ultérieur, notification de violation « sans retard injustifié » au sens de l'Art. 33(2), suppression des données 180 jours au plus après la fin de l'abonnement. Points 1 et 2 des actions ouvertes clos : ils avaient été tranchés le 2026-09-23 sans que le tableau soit mis à jour. |
