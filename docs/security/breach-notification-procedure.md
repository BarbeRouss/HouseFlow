# Procédure de notification de violation de données

**Articles 33 et 34 du RGPD**

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | HouseFlow (éditeur : BarbeRouss) |
| **Référent vie privée** | `privacy@houseflow.app` |
| **Contact sécurité** | `security@rouss.be` |
| **Date** | 2026-09-11 |
| **Version** | 1.0 |
| **Revue** | annuelle, à l'occasion de l'exercice de simulation |

> **À lire avant l'incident, pas pendant.** Le délai de 72 heures court **à compter de la prise de connaissance**, pas de la fin de l'investigation. Les décisions structurantes — qui notifie, à quelle autorité, avec quelles commandes de confinement — doivent être arrêtées **à froid**. Une procédure découverte le jour de l'incident ne sert à rien.

---

## Table des matières

1. [Qu'est-ce qu'une violation de données](#1--quest-ce-quune-violation-de-données)
2. [Rôles et responsabilités](#2--rôles-et-responsabilités)
3. [Détection](#3--détection)
4. [Arbre de décision](#4--arbre-de-décision)
5. [Délais et point de départ de l'horloge](#5--délais-et-point-de-départ-de-lhorloge)
6. [Mesures de confinement immédiates](#6--mesures-de-confinement-immédiates)
7. [Qualification du risque](#7--qualification-du-risque)
8. [Checklist des informations à collecter](#8--checklist-des-informations-à-collecter)
9. [Notification à l'autorité de contrôle](#9--notification-à-lautorité-de-contrôle-art-33)
10. [Communication aux personnes concernées](#10--communication-aux-personnes-concernées-art-34)
11. [Documentation et registre des violations](#11--documentation-et-registre-des-violations-art-335)
12. [Post-mortem](#12--post-mortem)
13. [Exercice de simulation annuel](#13--exercice-de-simulation-annuel)

---

## 1 — Qu'est-ce qu'une violation de données

L'article 4(12) définit la violation de données à caractère personnel comme « une violation de la sécurité entraînant, de manière accidentelle ou illicite, la **destruction**, la **perte**, l'**altération**, la **divulgation** non autorisée de données à caractère personnel transmises, conservées ou traitées d'une autre manière, ou l'**accès** non autorisé à de telles données ».

Cette définition recouvre **trois atteintes distinctes**, et il est essentiel de ne pas réduire la notion à la seule fuite de données.

| Type | Définition | Exemples concrets chez HouseFlow |
|---|---|---|
| **Confidentialité** | Divulgation ou accès non autorisé | Exfiltration de la base PostgreSQL ; compromission d'un compte d'administration Azure ; exposition de la clé `JWT__KEY` (permettant de forger des jetons pour n'importe quel compte) ; faille de contrôle d'accès (IDOR) permettant de lire les maisons d'un autre utilisateur ; secret publié par erreur dans le dépôt Git ; lien d'invitation diffusé publiquement ; export de production restauré dans un environnement de test sans `sanitize-pii.sh` |
| **Intégrité** | Altération non autorisée | Modification ou suppression massive de données par un compte compromis ; migration défectueuse corrompant des enregistrements ; injection altérant des données |
| **Disponibilité** | Destruction ou perte, même temporaire | **Ransomware** chiffrant la base ; suppression accidentelle d'une table ou d'une ressource Azure ; sauvegarde illisible au moment où l'on en a besoin ; indisponibilité prolongée empêchant les personnes d'accéder à leurs données |

> **Erreur classique.** La perte de disponibilité est une violation **au même titre** qu'une fuite. Un ransomware sur une base dont on possède une sauvegarde saine reste une violation : il faut l'évaluer, la documenter, et décider de notifier ou non.

**Ce qui n'est pas une violation** : un incident sans donnée personnelle en jeu (panne d'un environnement de prévisualisation alimenté par l'utilisateur de démonstration) ; une tentative d'intrusion **échouée** sans accès obtenu — à documenter en interne comme incident de sécurité, mais pas comme violation.

---

## 2 — Rôles et responsabilités

L'organisation est volontairement resserrée : dans une structure de cette taille, la dilution des responsabilités est le principal facteur de dépassement du délai de 72 heures.

| Rôle | Titulaire | Responsabilités |
|---|---|---|
| **Responsable de l'incident** | **L'éditeur (BarbeRouss)** | Pilote l'ensemble. Décide du confinement. **Décide de notifier ou non l'autorité, et de communiquer ou non aux personnes.** Signe la notification. Cette décision ne se délègue pas. |
| **Référent vie privée** | `privacy@houseflow.app` | Qualifie le risque, rédige la notification et la communication aux personnes, tient le [registre des violations](./breach-register.md), assure la liaison avec l'autorité de contrôle et répond aux personnes concernées. |
| **Responsable technique** | L'éditeur | Exécute le confinement, collecte les preuves, évalue le périmètre exact (quelles tables, combien d'enregistrements, quelles personnes), restaure le service. |
| **Sous-traitant — Microsoft** | Microsoft Ireland Operations Ltd | Notifie le responsable **sans retard injustifié** en cas de violation affectant ses services (Art. 28(3)(f) et 33(2)). Canaux : **Azure Service Health** (incidents de plateforme, alertes à configurer sur l'abonnement) et le **Microsoft Security Response Center**. Voir [subprocessors.md](../gdpr/subprocessors.md). |
| **Découvreur** | Toute personne : contributeur, utilisateur, chercheur en sécurité | **Signale immédiatement** à `security@rouss.be`. **Ne tente aucune investigation susceptible d'altérer les preuves.** |

**Règle de disponibilité.** Le responsable de l'incident et le référent vie privée étant la même personne, un **délai de contact maximal de 24 heures** est retenu comme hypothèse de dimensionnement. Si un incident est signalé un vendredi soir, la notification à 72 heures reste tenable — mais sans marge. En cas d'indisponibilité prévisible (congés), un **suppléant** doit être désigné et son nom consigné ici : *[à compléter]*.

---

## 3 — Détection

### 3.1 Sources de détection existantes

| Source | Ce qu'elle permet de voir | Où |
|---|---|---|
| **Journaux d'audit applicatifs** (`AuditLogs`) | Toute création, modification ou suppression d'entité, avec auteur, horodatage, IP, agent utilisateur et valeurs avant/après. **Source d'investigation principale** : c'est elle qui permet de déterminer le périmètre exact d'une violation. | Base PostgreSQL |
| **Journaux applicatifs Serilog** | Événements d'authentification : tentatives, échecs, succès ; erreurs applicatives ; démarrages. | Azure Container Apps → Log Analytics (30 jours) |
| **Journaux de plateforme Azure** | Accès aux ressources, opérations du plan de contrôle, connexions à PostgreSQL. | Azure Monitor / Log Analytics |
| **Rejets de limitation de débit** | Réponses HTTP 429 — indicateur direct de bourrage d'identifiants ou d'abus d'API. | Journaux applicatifs |
| **`RefreshTokens`** | Adresses IP de création et de révocation des sessions : une session ouverte depuis une IP ou un pays inhabituel est un signal fort. | Base PostgreSQL |
| **Signalement externe** | Chercheur en sécurité, utilisateur, hébergeur. | `security@rouss.be` |
| **Azure Service Health** | Incidents affectant les services Azure utilisés, y compris les violations déclarées par Microsoft. | Portail Azure |

### 3.2 Alertes recommandées à mettre en place

Aucune alerte automatisée n'est configurée à ce jour. Par ordre de priorité :

| # | Alerte | Seuil suggéré | Signal recherché | Source |
|---|---|---|---|---|
| 1 | **Échecs d'authentification massifs** | > 50 échecs en 5 minutes, ou > 20 échecs depuis une même IP en 1 minute | Bourrage d'identifiants, attaque par dictionnaire | Journaux Serilog / rejets 429 |
| 2 | **Exports de données inhabituels** | Plus de 3 entrées d'audit `Action = "DataExport"` en 1 heure, ou tout export sur un compte créé depuis moins de 24 heures | Exfiltration via l'API légitime — le canal le plus discret pour un attaquant disposant d'un compte compromis | `AuditLogs` |
| 3 | **Suppressions de comptes anormales** | Plus de 2 entrées `Action = "AccountDeleted"` en 1 heure | Destruction malveillante après compromission | `AuditLogs` |
| 4 | **Accès à la base depuis le bastion** | **Toute** connexion au bastion SSH | L'accès administratif direct à la base doit être exceptionnel et toujours intentionnel. Chaque connexion doit pouvoir être rattachée à une opération planifiée. | Journaux Azure Container Apps (`ca-bastion`) |
| 5 | **Modifications du plan de contrôle Azure** | Toute modification des règles réseau, des administrateurs Entra ID de PostgreSQL, ou du paramètre `public_network_access_enabled` | Compromission d'un compte d'administration Azure | Azure Activity Log |
| 6 | **Suppressions de masse** | Plus de 100 entrées d'audit `Action = "Deleted"` en 10 minutes pour un même utilisateur | Destruction de données, compte compromis ou exploitation d'une faille | `AuditLogs` |
| 7 | **Alertes de dépendances** | Toute vulnérabilité critique ou élevée | Vulnérabilité exploitable en production | Dependabot / `dotnet list package --vulnerable` — **à activer** |
| 8 | **Analyse de secrets du dépôt** | Tout secret détecté | Clé ou chaîne de connexion versionnée par erreur | GitHub secret scanning — **à activer** |

> Les points 7 et 8 figurent dans les [points ouverts du registre](../gdpr/processing-register.md#5-points-ouverts). Ce sont les deux mesures au meilleur rapport effort/bénéfice : activation en quelques minutes, couverture d'un vecteur d'incident fréquent.

---

## 4 — Arbre de décision

```
                    ┌──────────────────────────────────┐
                    │  SIGNAL : alerte, signalement,   │
                    │  comportement anormal constaté   │
                    └────────────────┬─────────────────┘
                                     │
                    ┌────────────────▼─────────────────┐
                    │  ÉTAPE 1 — CONFINEMENT           │
                    │  Agir AVANT de qualifier.        │
                    │  Stopper l'hémorragie,           │
                    │  préserver les preuves (§ 6).    │
                    └────────────────┬─────────────────┘
                                     │
                    ┌────────────────▼─────────────────┐
                    │  ÉTAPE 2 — S'agit-il d'une       │
                    │  violation au sens de l'Art.     │
                    │  4(12) ? (§ 1)                   │
                    │  Confidentialité / intégrité /   │
                    │  disponibilité                   │
                    └───────┬──────────────────┬───────┘
                        NON │                  │ OUI
                            │                  │
            ┌───────────────▼──────┐    ┌──────▼─────────────────────┐
            │ Incident de sécurité │    │  >>> L'HORLOGE DES 72 H    │
            │ Documenter en        │    │  DÉMARRE ICI (§ 5) <<<     │
            │ interne. Fin.        │    │  Ouvrir une fiche au       │
            └──────────────────────┘    │  registre des violations.  │
                                        └──────────────┬─────────────┘
                                                       │
                                        ┌──────────────▼─────────────┐
                                        │  ÉTAPE 3 — QUALIFIER LE    │
                                        │  RISQUE (grille du § 7)    │
                                        └──────────────┬─────────────┘
                                                       │
                 ┌─────────────────────────────────────┼──────────────────────────┐
                 │                                     │                          │
    ┌────────────▼───────────┐      ┌──────────────────▼──────────┐   ┌───────────▼──────────────┐
    │  RISQUE IMPROBABLE     │      │  RISQUE                     │   │  RISQUE ÉLEVÉ            │
    ├────────────────────────┤      ├─────────────────────────────┤   ├──────────────────────────┤
    │ Pas de notification    │      │ NOTIFIER l'autorité         │   │ NOTIFIER l'autorité      │
    │ à l'autorité.          │      │ ≤ 72 h (§ 9)                │   │ ≤ 72 h (§ 9)             │
    │                        │      │                             │   │            +             │
    │ MAIS documenter au     │      │ Pas de communication        │   │ COMMUNIQUER aux          │
    │ registre AVEC LA       │      │ individuelle obligatoire    │   │ personnes dans les       │
    │ MOTIVATION du non-     │      │ (sauf évolution du risque)  │   │ meilleurs délais (§ 10)  │
    │ signalement (Art.      │      │                             │   │ — sauf exception         │
    │ 33(5) est sans seuil)  │      │ Documenter au registre      │   │ Art. 34(3)               │
    └────────────┬───────────┘      └──────────────┬──────────────┘   └───────────┬──────────────┘
                 │                                 │                              │
                 └─────────────────────────────────┼──────────────────────────────┘
                                                   │
                                    ┌──────────────▼──────────────┐
                                    │  ÉTAPE 4 — DOCUMENTER       │
                                    │  Compléter le registre      │
                                    │  (Art. 33(5)) — § 11        │
                                    └──────────────┬──────────────┘
                                                   │
                                    ┌──────────────▼──────────────┐
                                    │  ÉTAPE 5 — POST-MORTEM      │
                                    │  Cause racine, correctifs,  │
                                    │  mise à jour de la          │
                                    │  procédure (§ 12)           │
                                    └─────────────────────────────┘
```

---

## 5 — Délais et point de départ de l'horloge

### 5.1 La prise de connaissance

L'article 33(1) impose la notification « **dans les meilleurs délais et, si possible, 72 heures au plus tard après en avoir pris connaissance** ».

**Le responsable est réputé avoir « pris connaissance » dès qu'il dispose d'un degré raisonnable de certitude qu'un incident de sécurité s'est produit et a compromis des données personnelles.** Ce n'est :

- **pas** la fin de l'investigation ;
- **pas** le moment où le périmètre exact est connu ;
- **pas** le moment où l'incident est corrigé.

| Situation | L'horloge démarre |
|---|---|
| Un chercheur signale une fuite avec preuve à l'appui | **À la réception du signalement**, dès vérification sommaire de sa plausibilité |
| Une alerte remonte un volume anormal d'exports | Au moment où une **première vérification** confirme que les exports ne sont pas légitimes |
| Un doute existe sur la réalité de l'incident | Pendant la **période de vérification initiale** (quelques heures, pas quelques jours) — puis l'horloge démarre si l'incident se confirme |
| Microsoft notifie une violation affectant ses services | **À la réception de la notification** de Microsoft |

> **Règle d'or.** En cas de doute, **considérer que l'horloge tourne**. Il vaut infiniment mieux notifier une violation qui s'avère ensuite bénigne, que dépasser le délai. Une notification excédentaire n'est pas sanctionnée ; un retard l'est.

### 5.2 Notification par phases

L'article 33(4) autorise expressément une notification **en plusieurs temps**, lorsque toutes les informations ne sont pas disponibles simultanément. C'est la norme, pas l'exception.

**Ne jamais attendre d'avoir tout compris pour notifier.** Envoyer dans les 72 heures une notification initiale, même incomplète, en indiquant explicitement quels éléments restent à établir et sous quel délai — puis compléter.

### 5.3 Dépassement du délai

Si les 72 heures sont dépassées, la notification doit être **accompagnée des motifs du retard** (Art. 33(1)). Les motifs recevables sont factuels et vérifiables : complexité technique de l'investigation, dépendance à l'analyse d'un sous-traitant, découverte tardive du périmètre réel. **Le motif n'efface pas le manquement**, mais son absence l'aggrave.

### 5.4 Chronologie cible

| Échéance | Action |
|---|---|
| **H+0** | Prise de connaissance. Ouverture d'une fiche au [registre des violations](./breach-register.md). Démarrage d'un journal horodaté de toutes les actions. |
| **H+0 à H+4** | **Confinement** (§ 6). Préservation des preuves. |
| **H+4 à H+24** | Détermination du périmètre : quelles données, combien de personnes, quelles catégories. Qualification du risque (§ 7). |
| **H+24 à H+48** | Décision de notification par le responsable de l'incident. Rédaction de la notification. |
| **H+48 à H+72** | **Notification à l'autorité de contrôle.** Si le risque est élevé : préparation et envoi de la communication aux personnes. |
| **H+72 à J+30** | Notifications complémentaires si nécessaire. Correction de la cause racine. Post-mortem. |

---

## 6 — Mesures de confinement immédiates

**Agir d'abord, qualifier ensuite.** Chaque minute de délai augmente le volume de données exposées.

> **Préserver les preuves.** Avant toute action destructive, **capturer** : journaux applicatifs de la fenêtre concernée, extrait des `AuditLogs` pertinents, journaux Azure. Une purge automatique ou un redéploiement peut effacer un élément décisif. Conserver ces captures hors production, dans un espace à accès restreint.

### 6.1 Suspicion de compromission de jetons ou de sessions

**Rotation de la clé de signature JWT** — invalide **instantanément tous les jetons d'accès en circulation**, y compris ceux détenus par un attaquant :

```bash
# Générer une nouvelle clé (≥ 32 caractères ; l'application refuse de démarrer en deçà)
openssl rand -base64 48

# Appliquer la nouvelle valeur à la variable d'environnement JWT__KEY
# de la Container App de l'API, puis redémarrer la révision.
# Effet : toute signature émise avec l'ancienne clé devient invalide.
```

**Révocation de toutes les sessions et clés API** — supprime tous les refresh tokens et révoque toutes les clés API :

```bash
dotnet HouseFlow.API.dll --revoke-all-sessions
```

> Ces deux mesures sont **complémentaires** : la rotation de `JWT__KEY` neutralise les jetons d'accès (durée de vie 15 minutes), tandis que `--revoke-all-sessions` empêche d'en obtenir de nouveaux et coupe l'accès par clé API. **Appliquer les deux** en cas de compromission avérée.
>
> **Conséquence assumée** : tous les utilisateurs sont déconnectés et doivent se reconnecter. C'est le prix du confinement, et il est bien moindre que celui d'une exfiltration prolongée.

### 6.2 Suspicion de compromission d'un compte d'administration Azure

```bash
# 1. Révoquer les sessions Entra ID du compte suspect
az ad user update --id <upn> --account-enabled false

# 2. Vérifier les administrateurs Entra ID de PostgreSQL — tout compte inattendu est un signal d'alarme
az postgres flexible-server ad-admin list \
  --resource-group rg-houseflow --server-name psql-houseflow

# 3. Retirer un administrateur illégitime
az postgres flexible-server ad-admin delete \
  --resource-group rg-houseflow --server-name psql-houseflow --object-id <objectId>

# 4. Auditer les opérations récentes du plan de contrôle
az monitor activity-log list --resource-group rg-houseflow --offset 7d --output table
```

### 6.3 Suspicion d'exfiltration en cours par le réseau

```bash
# Vérifier que l'accès public à la base est bien désactivé (valeur attendue : false)
az postgres flexible-server show \
  --resource-group rg-houseflow --name psql-houseflow \
  --query "network.publicNetworkAccess"

# En cas d'activation illégitime, la désactiver immédiatement
az postgres flexible-server update \
  --resource-group rg-houseflow --name psql-houseflow \
  --public-network-access Disabled

# Arrêter le bastion SSH s'il n'est pas nécessaire à l'investigation
az containerapp update --name ca-bastion --resource-group rg-houseflow --min-replicas 0
```

En dernier recours, **couper l'ingress** de l'API pour interrompre tout accès pendant l'investigation :

```bash
az containerapp ingress disable --name <api-container-app> --resource-group rg-houseflow
```

### 6.4 Rotation des secrets

| Secret | Emplacement | Effet de la rotation |
|---|---|---|
| `JWT__KEY` | Variable d'environnement de la Container App de l'API | Invalide tous les jetons d'accès |
| Identifiants de connexion PostgreSQL | **Sans objet** — l'authentification est assurée par **Entra ID sans mot de passe** (`password_auth_enabled = false`). Il n'existe aucun mot de passe de base de données à faire fuiter, ni à changer. | — |
| Secrets GitHub Actions | Paramètres du dépôt → Secrets | Déploiement — l'authentification vers Azure repose sur **OIDC**, sans secret de longue durée |
| Identités managées Azure | Portail Azure / `az` | Accès de l'application aux ressources |
| Clé publique SSH du bastion | Secret de la Container App `ca-bastion` | Accès administratif à la base |

### 6.5 Perte de disponibilité — ransomware, suppression accidentelle

```bash
# Lister les points de restauration disponibles (rétention PITR : 7 jours)
az postgres flexible-server show \
  --resource-group rg-houseflow --name psql-houseflow \
  --query "{earliest:backup.earliestRestoreDate, retention:backup.backupRetentionDays}"

# Restaurer vers un NOUVEAU serveur — ne jamais écraser l'original,
# qui constitue une pièce à conviction
az postgres flexible-server restore \
  --resource-group rg-houseflow \
  --name psql-houseflow-restore \
  --source-server psql-houseflow \
  --restore-time "2026-09-11T02:00:00Z"
```

> **Avant toute remise en service d'une base restaurée** : réappliquer les suppressions de comptes intervenues entre la date du point de restauration et la date de restauration. À défaut, des données qu'une personne avait demandé d'effacer seraient réintroduites — ce qui constituerait un **nouveau manquement** à l'article 17. Voir la [politique de conservation, § 3.1](../gdpr/data-retention-policy.md#31-articulation-avec-le-droit-à-leffacement).
>
> **Rétention de 7 jours** : passé ce délai, aucune restauration n'est possible. Un incident découvert tardivement peut donc être irréversible — argument supplémentaire en faveur des alertes du § 3.2.

---

## 7 — Qualification du risque

L'évaluation porte sur le risque **pour les droits et libertés des personnes concernées**, et non sur le préjudice pour l'éditeur.

### 7.1 Grille d'évaluation

| Critère | Questions | Aggravant | Atténuant |
|---|---|---|---|
| **Nature des données** | Quelles catégories ? | Email + nom + adresse du logement combinés (permettent d'identifier et de localiser une personne à son domicile) ; jetons de session exploitables ; contenu des champs `Notes` | Identifiants techniques seuls ; données déjà anonymisées ; journaux d'audit de plus d'un an (anonymisés) |
| **Volume** | Combien de personnes ? Combien d'enregistrements ? | L'ensemble de la base | Un compte unique ; un périmètre circonscrit et identifié |
| **Facilité d'identification** | Les personnes sont-elles directement identifiables ? | Email et nom en clair | Données pseudonymisées ; IP tronquées ; identifiants opaques seuls |
| **Gravité des conséquences** | Que peut faire un attaquant ? | Usurpation de compte ; connaissance de l'adresse du domicile et de l'état des équipements (**risque physique** : savoir qu'une alarme est en panne, ou déduire une absence prolongée) ; hameçonnage ciblé et crédible ; perte définitive de données | Aucun accès exploitable ; données sans valeur pour un attaquant |
| **Protection préalable (Art. 34(3)(a))** | Les données étaient-elles **rendues incompréhensibles** ? | Données en clair | **Mots de passe hachés en BCrypt** ; **clés API hachées en SHA-256** ; **refresh tokens hachés** — ces éléments sont inexploitables en l'état |
| **Caractéristiques des personnes** | Personnes vulnérables ? | — | Utilisateurs adultes, dans une relation contractuelle volontaire |
| **Réversibilité** | La situation peut-elle être rétablie ? | Exfiltration (irréversible par nature) | Perte de disponibilité avec sauvegarde saine restaurée rapidement |
| **Persistance de l'accès** | L'attaquant a-t-il encore accès ? | Accès non confiné | Accès coupé, jetons révoqués, secrets tournés |

### 7.2 Détermination du niveau

| Niveau | Définition | Obligations |
|---|---|---|
| **Improbable** | La violation est **peu susceptible d'engendrer un risque** pour les droits et libertés. | **Pas de notification.** Documentation obligatoire au registre, **avec la motivation du non-signalement**. |
| **Risque** | Un risque existe, sans être élevé. | **Notification à l'autorité ≤ 72 h.** Pas de communication individuelle obligatoire. |
| **Risque élevé** | Conséquences significatives probables : usurpation d'identité, préjudice matériel ou moral, atteinte à la vie privée, risque physique. | **Notification à l'autorité ≤ 72 h** **+** **communication aux personnes** dans les meilleurs délais. |

### 7.3 Scénarios de référence

Pré-qualification à froid, à ajuster selon les circonstances réelles.

| Scénario | Niveau | Motivation |
|---|---|---|
| **Exfiltration complète de la base** | **Risque élevé** | Emails, noms et adresses de logements en clair. Le hachage BCrypt des mots de passe **ne dispense pas** de la communication : l'article 34(3)(a) n'est satisfait que si **toutes** les données concernées sont rendues incompréhensibles. Hameçonnage ciblé et risque physique (connaissance du domicile et de l'état des équipements). |
| **Clé `JWT__KEY` exposée** | **Risque élevé** | Permet de forger un jeton valide pour **n'importe quel compte** : usurpation totale, sans limite. Confinement impératif par rotation immédiate. |
| **Compromission d'un compte d'administration Azure** | **Risque élevé** | Accès potentiel à l'ensemble de l'infrastructure et de la base. |
| **Faille de contrôle d'accès (IDOR) exploitée** | **Risque** à **élevé** | Selon le volume réellement consulté, à établir par les `AuditLogs`. Élevé si l'exploitation a été massive. |
| **Ransomware avec restauration réussie sous 24 h** | **Risque** | Perte de disponibilité temporaire. **Devient un risque élevé** si les données ont également été exfiltrées (double extorsion — hypothèse à retenir par défaut en l'absence de preuve contraire). |
| **Suppression accidentelle restaurée sans perte** | **Improbable** à **Risque** | Improbable si la restauration est intégrale et rapide. Risque si des données sont définitivement perdues. |
| **Secret publié dans le dépôt Git, retiré sous 1 h, sans accès constaté** | **Improbable** | Documenter, faire tourner le secret par précaution, vérifier l'absence d'utilisation dans les journaux. |
| **Lien d'invitation diffusé publiquement** | **Improbable** | Portée limitée à une maison ; révocation immédiate ; divulgation minimale avant acceptation (nom de la maison, rôle, identité de l'invitant). |
| **Copie de production déployée en préproduction sans `sanitize-pii.sh`** | **Risque** | Données réelles exposées dans un environnement moins protégé. Périmètre à établir : qui y a eu accès, pendant combien de temps. |

---

## 8 — Checklist des informations à collecter

À renseigner dès l'ouverture de l'incident. Les champs marqués **[33(3)]** sont exigés par la notification à l'autorité.

### Identification
- [ ] Référence de l'incident (format `VIOL-AAAA-NN`)
- [ ] **Date et heure de la découverte** (UTC) — départ de l'horloge des 72 h
- [ ] Date et heure de **survenance** (estimée si inconnue)
- [ ] La violation est-elle **terminée**, **en cours**, ou **récurrente** ?
- [ ] Comment a-t-elle été découverte ? (alerte, signalement externe, constat fortuit)
- [ ] Qui a découvert l'incident ?

### Nature **[33(3)(a)]**
- [ ] Type d'atteinte : **confidentialité** / **intégrité** / **disponibilité** (plusieurs possibles)
- [ ] Description factuelle des faits, sans interprétation
- [ ] Cause : malveillance externe, malveillance interne, erreur humaine, défaillance technique, sous-traitant
- [ ] Vecteur technique : vulnérabilité applicative, compte compromis, secret exposé, erreur de configuration

### Périmètre **[33(3)(a)]**
- [ ] **Catégories de données** concernées : identification (email, nom, prénom), authentification (hachages, jetons), données de maison (adresse, équipements), données d'entretien (coûts, prestataires, notes), données techniques (IP, agent utilisateur), journaux d'audit
- [ ] **Nombre approximatif de personnes concernées**
- [ ] **Nombre approximatif d'enregistrements** concernés
- [ ] **Catégories de personnes** : utilisateurs, collaborateurs, locataires, prestataires cités
- [ ] Les personnes concernées sont-elles **identifiables** ? Résident-elles dans plusieurs États membres ?
- [ ] Les données étaient-elles **chiffrées ou hachées** ? Lesquelles précisément ?

### Conséquences **[33(3)(c)]**
- [ ] Conséquences probables pour les personnes (usurpation, hameçonnage, perte de données, risque physique)
- [ ] Niveau de risque retenu, avec la **motivation** issue de la grille du § 7
- [ ] Un préjudice s'est-il déjà matérialisé ?

### Mesures **[33(3)(d)]**
- [ ] Mesures de **confinement** prises, avec horodatage de chacune
- [ ] Mesures **correctives** appliquées ou prévues, avec échéance
- [ ] Mesures d'**atténuation** proposées aux personnes
- [ ] Mesures **préventives** pour éviter la récurrence

### Décisions
- [ ] Notification à l'autorité : **oui / non** — avec **motivation dans les deux cas**
- [ ] Autorité saisie et date d'envoi ; numéro d'accusé de réception
- [ ] Communication aux personnes : **oui / non** — avec motivation ; si non, exception de l'Art. 34(3) invoquée
- [ ] Date de la communication ; canal utilisé
- [ ] Un sous-traitant est-il impliqué ? A-t-il notifié, et quand ?

### Preuves
- [ ] Extraits de journaux conservés (applicatifs, `AuditLogs`, Azure), avec leur emplacement d'archivage
- [ ] Captures d'écran, messages de signalement
- [ ] Journal horodaté de toutes les actions entreprises

---

## 9 — Notification à l'autorité de contrôle (Art. 33)

### 9.1 Quelle autorité

Le **guichet unique** (Art. 56) désigne l'autorité de l'**établissement principal** du responsable de traitement.

> **Décision à arrêter avant tout incident.** L'établissement principal de HouseFlow doit être formellement déterminé — voir l'[arbitrage § 6.2 de la politique de conservation](../gdpr/data-retention-policy.md#62--autorité-de-contrôle-chef-de-file--à-confirmer). Rechercher le bon guichet pendant les 72 heures est une perte de temps inacceptable.

| Autorité | Canal de notification | Coordonnées |
|---|---|---|
| **France — CNIL** | Téléservice « Notifier une violation de données personnelles » : **https://notifications.cnil.fr/notifications/index** | 3 Place de Fontenoy, TSA 80715, 75334 Paris Cedex 07 — 01 53 73 22 22 |
| **Belgique — APD / GBA** | Formulaire de notification en ligne : **https://www.autoriteprotectiondonnees.be/professionnel/actions/violation-de-donnees-personnelles** | Rue de la Presse 35, 1000 Bruxelles — +32 (0)2 274 48 00 — `contact@apd-gba.be` |

Le téléservice CNIL délivre un **accusé de réception** : le conserver et en consigner la référence au registre. Il permet une notification initiale, puis des compléments ultérieurs — mécanisme correspondant exactement à la notification par phases de l'Art. 33(4).

### 9.2 Modèle de notification

> **NOTIFICATION D'UNE VIOLATION DE DONNÉES À CARACTÈRE PERSONNEL**
> *Article 33 du Règlement (UE) 2016/679*
>
> **Référence interne** : VIOL-AAAA-NN
> **Type de notification** : ☐ initiale ☐ complémentaire ☐ définitive
>
> ---
>
> **1. RESPONSABLE DE TRAITEMENT**
>
> - Dénomination : **HouseFlow**
> - Éditeur : **BarbeRouss**
> - Adresse : *[à compléter]*
> - Secteur d'activité : édition de logiciel — service en ligne de suivi de maintenance immobilière
>
> **2. POINT DE CONTACT** *(Art. 33(3)(b))*
>
> - Aucun délégué à la protection des données n'est désigné : la désignation n'est pas obligatoire au titre de l'article 37(1) (analyse documentée dans notre registre des traitements).
> - **Référent vie privée** : `privacy@houseflow.app`
> - Contact sécurité : `security@rouss.be`
>
> **3. NATURE DE LA VIOLATION** *(Art. 33(3)(a))*
>
> - Date et heure de **survenance** : `[JJ/MM/AAAA HH:MM UTC]` (estimée / confirmée)
> - Date et heure de **prise de connaissance** : `[JJ/MM/AAAA HH:MM UTC]`
> - Statut : ☐ terminée ☐ en cours ☐ récurrente
> - Type d'atteinte : ☐ confidentialité ☐ intégrité ☐ disponibilité
> - Origine : ☐ malveillance externe ☐ malveillance interne ☐ erreur humaine ☐ défaillance technique ☐ sous-traitant
> - **Description des circonstances** : `[exposé factuel : ce qui s'est passé, comment cela a été rendu possible, comment cela a été découvert]`
>
> **4. CATÉGORIES ET NOMBRE DE PERSONNES CONCERNÉES** *(Art. 33(3)(a))*
>
> - Catégories de personnes : `[utilisateurs inscrits / collaborateurs / locataires / prestataires cités]`
> - **Nombre approximatif de personnes** : `[N]`
> - Résidence : ☐ France ☐ Belgique ☐ autres États membres : `[…]`
>
> **5. CATÉGORIES ET NOMBRE D'ENREGISTREMENTS CONCERNÉS** *(Art. 33(3)(a))*
>
> - Catégories de données : `[identification : email, nom, prénom / authentification : empreintes de mots de passe, jetons / données de logement : adresse, équipements / données d'entretien : dates, coûts, prestataires, notes / données techniques : adresses IP, agents utilisateurs / journaux d'audit]`
> - **Nombre approximatif d'enregistrements** : `[N]`
> - **Aucune donnée relevant de l'article 9** (catégories particulières) ni de l'**article 10** n'est traitée par le service.
> - **Mesures de protection préalables** : les mots de passe sont hachés en BCrypt, les clés d'API en SHA-256 et les jetons de rafraîchissement sont stockés hachés — ces éléments sont inexploitables en l'état. `[Préciser les données qui étaient, elles, en clair.]`
>
> **6. CONSÉQUENCES PROBABLES** *(Art. 33(3)(c))*
>
> `[Conséquences pour les personnes : risque d'usurpation de compte, de hameçonnage ciblé, de divulgation de l'adresse du domicile, d'atteinte à la vie privée, de perte de données. Préciser le niveau de risque retenu et sa motivation.]`
>
> **7. MESURES PRISES OU PROPOSÉES** *(Art. 33(3)(d))*
>
> - **Mesures de confinement** : `[rotation de la clé de signature des jetons, révocation de l'ensemble des sessions et des clés d'API, rotation des secrets, restriction réseau, restauration — avec l'horodatage de chaque action]`
> - **Mesures correctives** : `[correction de la vulnérabilité, déploiement, vérifications]`
> - **Mesures d'atténuation pour les personnes** : `[recommandation de changement de mot de passe, mise en garde contre le hameçonnage]`
> - **Mesures préventives** : `[alertes mises en place, revue de code, audit]`
>
> **8. COMMUNICATION AUX PERSONNES CONCERNÉES** *(Art. 34)*
>
> - ☐ Effectuée le `[date]`, par `[canal]` — `[N]` personnes touchées
> - ☐ Prévue le `[date]`
> - ☐ Non effectuée — motif : `[exception de l'article 34(3)(a), (b) ou (c), à préciser et à justifier]`
>
> **9. SOUS-TRAITANT**
>
> - ☐ Sans objet
> - ☐ Sous-traitant impliqué : `[dénomination]` — notification reçue le `[date]`
>
> **10. MOTIF DU RETARD** *(si la notification intervient au-delà de 72 heures — Art. 33(1))*
>
> `[Motif factuel et vérifiable.]`

---

## 10 — Communication aux personnes concernées (Art. 34)

### 10.1 Quand communiquer

La communication est **obligatoire** lorsque la violation est susceptible d'engendrer un **risque élevé** pour les droits et libertés des personnes. Elle doit intervenir **dans les meilleurs délais** — sans attendre les 72 heures de la notification à l'autorité si le risque est immédiat.

### 10.2 Exceptions (Art. 34(3))

| Exception | Condition | Applicabilité à HouseFlow |
|---|---|---|
| **(a)** Mesures de protection appropriées appliquées **avant** la violation, notamment un **chiffrement** rendant les données **incompréhensibles** | Le chiffrement doit être **antérieur** à la violation et robuste | **Rarement applicable.** Les mots de passe sont hachés en BCrypt, mais **les emails, noms, adresses de logements et données d'entretien sont stockés en clair** en base. Une exfiltration de la base ne bénéficie donc **pas** de cette exception. Elle ne pourrait être invoquée que pour une violation portant **exclusivement** sur des données hachées (fuite de la seule colonne `PasswordHash`, par exemple). |
| **(b)** Mesures ultérieures garantissant que le **risque élevé n'est plus susceptible de se matérialiser** | Les mesures doivent neutraliser le risque, pas seulement le réduire | Invocable, par exemple, lorsque des jetons ont fuité mais que la rotation de `JWT__KEY` et `--revoke-all-sessions` ont été appliqués **avant toute exploitation constatée** — sous réserve que les journaux le démontrent. |
| **(c)** **Efforts disproportionnés** | Remplacer la communication individuelle par une **communication publique ou une mesure équivalente** | Peu probable : la base d'utilisateurs est limitée et les adresses email sont connues. Serait invocable si les emails eux-mêmes étaient devenus inexploitables. |

> **Toute exception invoquée doit être motivée par écrit au registre**, et elle est susceptible d'être contestée par l'autorité — qui peut **exiger** la communication (Art. 34(4)).

### 10.3 Contenu obligatoire (Art. 34(2))

En **termes clairs et simples**, avec au minimum les éléments de l'Art. 33(3) **(b)**, **(c)** et **(d)** :

- le **point de contact** auprès duquel obtenir davantage d'informations ;
- les **conséquences probables** de la violation ;
- les **mesures prises ou proposées** pour y remédier et en atténuer les effets.

> **Le jargon juridique est proscrit.** Une communication que la personne ne comprend pas ne remplit pas son office : elle doit permettre d'**agir**.

### 10.4 Modèle d'email — français

> **Objet : Important — incident de sécurité concernant vos données HouseFlow**
>
> Bonjour,
>
> Nous vous écrivons pour vous informer d'un incident de sécurité qui a touché HouseFlow et qui concerne vos données personnelles. Nous sommes sincèrement désolés de cette situation.
>
> **Ce qui s'est passé**
>
> Le [date], nous avons découvert que [description en langage courant : par exemple, « une personne non autorisée a pu accéder à notre base de données »]. Nous avons immédiatement pris des mesures pour mettre fin à cet accès.
>
> **Les données concernées**
>
> Les informations suivantes vous concernant ont pu être consultées :
>
> - [par exemple : votre adresse email, votre nom et votre prénom]
> - [par exemple : l'adresse et les équipements de vos logements enregistrés]
> - [par exemple : l'historique de vos entretiens, leurs dates et leurs coûts]
>
> **Votre mot de passe n'est pas concerné** : il n'est jamais conservé en clair, mais sous une forme chiffrée irréversible. [À adapter si le mot de passe est concerné.]
>
> **Ce que nous avons fait**
>
> - [par exemple : nous avons coupé l'accès de l'attaquant le jour même]
> - [par exemple : nous avons déconnecté toutes les sessions et renouvelé toutes les clés de sécurité]
> - [par exemple : nous avons corrigé la faille à l'origine de l'incident]
> - Nous avons informé l'autorité de protection des données compétente.
>
> **Ce que nous vous recommandons de faire**
>
> 1. **Changez votre mot de passe HouseFlow**, depuis les paramètres de votre compte.
> 2. **Si vous utilisiez ce même mot de passe ailleurs, changez-le également** sur ces autres services. C'est la précaution la plus importante.
> 3. **Soyez vigilant face aux emails suspects.** Une personne disposant de votre adresse et de votre nom peut tenter de se faire passer pour nous. **Nous ne vous demanderons jamais votre mot de passe par email**, et nous ne vous enverrons jamais de lien vous demandant de le saisir.
> 4. [Si l'adresse du logement est concernée : **Restez attentif à toute sollicitation inhabituelle** faisant référence à votre logement ou à vos équipements.]
>
> **Pour nous contacter**
>
> Pour toute question sur cet incident et sur vos données, écrivez-nous à **privacy@houseflow.app**. Nous répondons à chaque message.
>
> Vous pouvez également introduire une réclamation auprès de l'autorité de protection des données de votre pays de résidence :
>
> - **France — CNIL** : [cnil.fr/fr/plaintes](https://www.cnil.fr/fr/plaintes)
> - **Belgique — Autorité de protection des données** : [autoriteprotectiondonnees.be](https://www.autoriteprotectiondonnees.be)
>
> Nous mesurons la confiance que vous nous accordez en nous confiant ces informations, et nous regrettons de ne pas l'avoir pleinement honorée. Nous avons renforcé nos protections pour que cela ne se reproduise pas.
>
> L'équipe HouseFlow

### 10.5 Modèle d'email — anglais

> **Subject: Important — security incident affecting your HouseFlow data**
>
> Hello,
>
> We are writing to let you know about a security incident at HouseFlow that affected your personal data. We are genuinely sorry this happened.
>
> **What happened**
>
> On [date], we discovered that [plain-language description: for example, "an unauthorised party was able to access our database"]. We acted immediately to shut down that access.
>
> **What data was involved**
>
> The following information about you may have been accessed:
>
> - [for example: your email address, first name and last name]
> - [for example: the address and equipment of the homes you registered]
> - [for example: your maintenance history, including dates and costs]
>
> **Your password was not affected**: we never store it in readable form, only as an irreversible cryptographic fingerprint. [Adjust if passwords are affected.]
>
> **What we have done**
>
> - [for example: we cut off the attacker's access the same day]
> - [for example: we signed out every session and rotated all security keys]
> - [for example: we fixed the flaw that made this possible]
> - We have notified the competent data protection authority.
>
> **What we recommend you do**
>
> 1. **Change your HouseFlow password** in your account settings.
> 2. **If you used that same password anywhere else, change it there too.** This is the single most important step.
> 3. **Be cautious with unexpected emails.** Someone holding your name and address may try to impersonate us. **We will never ask for your password by email**, and we will never send you a link asking you to enter it.
> 4. [If home addresses are affected: **Stay alert to unusual approaches** referring to your home or its equipment.]
>
> **How to reach us**
>
> For any question about this incident or your data, write to **privacy@houseflow.app**. We reply to every message.
>
> You also have the right to lodge a complaint with the data protection authority in your country of residence:
>
> - **France — CNIL**: [cnil.fr/fr/plaintes](https://www.cnil.fr/fr/plaintes)
> - **Belgium — Data Protection Authority**: [autoriteprotectiondonnees.be](https://www.autoriteprotectiondonnees.be)
>
> We understand the trust you place in us when you share this information, and we are sorry we did not fully live up to it. We have strengthened our safeguards so this does not happen again.
>
> The HouseFlow team

### 10.6 Canaux

| Canal | Usage |
|---|---|
| **Email individuel** | **Canal principal** — direct, traçable, permet un message personnalisé au périmètre réellement concerné. |
| **Bandeau in-app** | **Complément indispensable** : les emails d'incident finissent fréquemment en indésirables, et l'envoi d'emails transactionnels n'est pas encore implémenté — le bandeau est donc, en l'état, le canal le plus fiable. |
| **Page publique dédiée** | Pour une violation de grande ampleur, ou au titre de l'exception de l'Art. 34(3)(c). |

> **Contrainte opérationnelle à connaître.** L'envoi d'emails transactionnels **n'est pas implémenté** à ce jour. En cas d'incident nécessitant une communication individuelle, l'envoi devra être réalisé **manuellement** depuis la messagerie de l'éditeur, à partir de la liste des adresses concernées extraite de la base. **Cette contrainte doit être anticipée** : elle est lente et sujette à erreur pour un volume important. L'exercice de simulation annuel (§ 13) doit inclure ce point.

---

## 11 — Documentation et registre des violations (Art. 33(5))

L'article 33(5) impose de **documenter toute violation**, y compris celles qui ne sont pas notifiées : les faits, leurs effets et les mesures correctives prises. **Cette obligation est sans seuil et sans exception.**

Le registre est tenu dans un document séparé : **[`breach-register.md`](./breach-register.md)**.

**Pourquoi un document distinct.** Le registre est la **première pièce demandée** lors d'un contrôle consécutif à un incident. Un registre vide et daté démontre qu'aucune violation n'est survenue ; un registre inexistant laisse penser qu'elles n'ont simplement pas été documentées — ce qui constitue un manquement autonome à l'article 33(5), indépendamment de la gravité de l'incident lui-même.

**Règle.** Une fiche est ouverte au registre **dès la prise de connaissance**, avant même la qualification. Une violation qualifiée « improbable » et non notifiée doit y figurer **avec la motivation écrite** du non-signalement : c'est précisément cette motivation que l'autorité examinera pour vérifier le respect de l'article 33(1).

---

## 12 — Post-mortem

À réaliser dans les **30 jours** suivant la clôture de l'incident, quelle que soit sa gravité. Il est **sans recherche de faute** : l'objectif est d'identifier la défaillance systémique, pas la personne.

### Trame

1. **Chronologie factuelle** — de la survenance à la clôture, horodatée. Y faire apparaître les délais de détection, de confinement et de notification : ce sont eux qu'il faut réduire.
2. **Cause racine** — méthode des cinq « pourquoi ». S'arrêter à « erreur humaine » est un échec d'analyse : pourquoi cette erreur a-t-elle été possible, et pourquoi n'a-t-elle pas été rattrapée ?
3. **Ce qui a fonctionné** — à consigner explicitement, afin de ne pas dégrader ces mécanismes lors des corrections.
4. **Ce qui n'a pas fonctionné** — détection tardive, confinement incomplet, procédure inapplicable en l'état, information manquante, commande erronée.
5. **Actions correctives** — chacune avec un responsable et une échéance :
   - correction technique de la cause racine ;
   - alerte permettant une détection plus précoce d'un incident du même type ;
   - test automatisé prévenant la régression ;
   - mise à jour de la présente procédure.
6. **Mise à jour de la documentation** — présente procédure, [registre des traitements](../gdpr/processing-register.md) si les mesures de sécurité évoluent, [SECURITY.md](../../SECURITY.md).

Le compte rendu est consigné dans la fiche correspondante du [registre des violations](./breach-register.md).

---

## 13 — Exercice de simulation annuel

L'article 32(1)(d) impose de **tester, analyser et évaluer régulièrement l'efficacité** des mesures. Une procédure jamais éprouvée échoue le jour où elle sert.

| Élément | Valeur |
|---|---|
| **Périodicité** | **Annuelle** |
| **Durée** | Une demi-journée |
| **Participants** | Éditeur (responsable de l'incident et référent vie privée) ; tout contributeur ayant accès à la production |
| **Format** | Exercice sur table — **aucune action réelle en production** |
| **Dernier exercice réalisé** | *[à compléter]* |
| **Prochain exercice prévu** | *[à compléter]* |

### 13.1 Scénario de référence

> **T+0 — Lundi, 22 h 15.** Un chercheur en sécurité écrit à `security@rouss.be`. Il indique avoir trouvé sur un forum un extrait de 500 lignes présenté comme provenant de la base HouseFlow. L'extrait, joint à son message, contient des adresses email, des noms et des adresses postales qui paraissent authentiques. Il précise que le message d'origine du forum est daté de **cinq jours** auparavant.
>
> **Contraintes de l'exercice** : les alertes automatisées ne sont pas en place ; l'envoi d'emails transactionnels n'est pas implémenté ; la rétention PITR est de 7 jours — l'incident daterait de 5 jours.

### 13.2 Checklist de l'exercice

**Détection et prise de connaissance**
- [ ] À quelle date et heure précises l'horloge des 72 heures démarre-t-elle ? Quelle est l'échéance exacte de notification ?
- [ ] Quelle vérification de plausibilité minimale est menée avant de déclencher la procédure ?
- [ ] Quels journaux consulter en priorité, et **sont-ils encore disponibles** cinq jours après les faits ? (Log Analytics : 30 jours ✓ ; `AuditLogs` : 1 an ✓ ; PITR : 7 jours — **marge de 2 jours seulement**)

**Confinement**
- [ ] Quelles commandes exécuter, dans quel ordre ? Les retrouver dans le § 6 et vérifier qu'elles sont exactes et exécutables.
- [ ] Qui dispose effectivement des accès Azure nécessaires ? Ces accès fonctionnent-ils un lundi à 22 h ?
- [ ] Les preuves ont-elles été capturées **avant** toute action destructive ?

**Qualification**
- [ ] Appliquer la grille du § 7. Quel niveau de risque ? Avec quelle motivation écrite ?
- [ ] L'exception de l'Art. 34(3)(a) est-elle invocable ? *(Réponse attendue : non — emails, noms et adresses sont en clair en base.)*

**Notification**
- [ ] Quelle autorité saisir ? Le point est-il tranché ? *(Si la réponse n'est pas immédiate, c'est le principal enseignement de l'exercice.)*
- [ ] Remplir le modèle du § 9.2 avec les informations disponibles. Quels champs demeurent inconnus ? Comment le formuler dans une notification initiale au sens de l'Art. 33(4) ?
- [ ] Le délai de 72 heures est-il tenu ? Sinon, quel motif de retard serait recevable ?

**Communication aux personnes**
- [ ] Combien de personnes sont concernées ? Comment extraire la liste des adresses email ?
- [ ] **L'envoi d'emails n'étant pas implémenté, comment procéder concrètement ?** Combien de temps cela prend-il ? Quelle solution de repli ? *(Bandeau in-app, envoi manuel par lots.)*
- [ ] Adapter le modèle du § 10.4 au scénario.

**Documentation**
- [ ] Ouvrir une fiche fictive au [registre des violations](./breach-register.md), la remplir intégralement, puis la retirer à l'issue de l'exercice.

### 13.3 Compte rendu d'exercice

| Élément | À consigner |
|---|---|
| Date et participants | |
| Scénario utilisé | |
| **Délai simulé de prise de connaissance à la notification** | |
| Écarts constatés dans la procédure | |
| Commandes ou informations erronées ou manquantes | |
| Actions correctives, avec responsable et échéance | |
| Mises à jour apportées à la présente procédure | |

| Date | Scénario | Délai simulé | Écarts identifiés | Actions correctives |
|---|---|---|---|---|
| *(aucun exercice réalisé à ce jour)* | | | | |

---

## 14 — Documents liés

- [Registre des violations de données](./breach-register.md) — Art. 33(5)
- [Index du dossier de conformité RGPD](../gdpr/README.md)
- [Registre des activités de traitement](../gdpr/processing-register.md) — mesures de sécurité, annexe C
- [Politique de conservation des données](../gdpr/data-retention-policy.md) — sauvegardes et restauration
- [Sous-traitants et destinataires](../gdpr/subprocessors.md) — obligation de notification de Microsoft
- [SECURITY.md](../../SECURITY.md) — mesures de sécurité et signalement de vulnérabilité
