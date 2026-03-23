# User Stories - House Flow

## MVP - Authentification

### US-001: Inscription
**En tant que** visiteur
**Je veux** créer un compte avec mon email et mot de passe
**Afin de** pouvoir utiliser l'application

**Critères d'acceptation:**
- Formulaire avec prénom, nom, email, mot de passe
- Validation email unique
- Mot de passe min 12 caractères avec majuscule, minuscule, chiffre et caractère spécial
- Redirection vers page d'ajout d'appareil après inscription
- Création automatique d'une première maison "Ma maison"

---

### US-002: Connexion
**En tant que** utilisateur inscrit
**Je veux** me connecter avec mon email et mot de passe
**Afin de** accéder à mes données

**Critères d'acceptation:**
- Formulaire email + mot de passe
- Message d'erreur si identifiants incorrects
- Redirection vers dashboard après connexion
- Token JWT stocké en localStorage (persistance après refresh de page)
- Refresh token en cookie HttpOnly pour renouvellement automatique

---

### US-003: Déconnexion
**En tant que** utilisateur connecté
**Je veux** me déconnecter
**Afin de** sécuriser mon compte

**Critères d'acceptation:**
- Bouton de déconnexion accessible
- Suppression du token (localStorage et cookie)
- Redirection vers page de connexion

---

## MVP - Dashboard

### US-010: Voir mes maisons
**En tant que** utilisateur connecté
**Je veux** voir la liste de toutes mes maisons
**Afin de** avoir une vue d'ensemble

**Critères d'acceptation:**
- Liste des maisons avec nom et adresse
- Score de progression par maison (%) avec cercle de score visuel
- Badge "OK" si 100%
- Badge "X en attente" et "X en retard" si entretiens non à jour
- Score global affiché (moyenne pondérée)

---

### US-011: Dashboard vide
**En tant que** nouvel utilisateur
**Je veux** voir un état vide accueillant
**Afin de** comprendre comment démarrer

**Critères d'acceptation:**
- Message explicatif
- Bouton d'action pour ajouter une maison

---

### US-012: Ajouter une maison
**En tant que** utilisateur
**Je veux** ajouter une nouvelle maison
**Afin de** gérer plusieurs propriétés

**Critères d'acceptation:**
- Page dédiée avec formulaire
- Champs: nom (obligatoire), adresse, code postal, ville
- La maison apparaît dans la liste après création

---

## MVP - Maison

### US-020: Voir détail maison
**En tant que** utilisateur
**Je veux** voir le détail d'une maison
**Afin de** gérer ses appareils

**Critères d'acceptation:**
- Nom et adresse de la maison
- Score de la maison (%) avec cercle de score visuel
- Liste des appareils triés par priorité (en retard → en attente → à jour)
- Badge statut par appareil (À jour / À faire / En retard)
- Barre de progression par appareil

---

### US-021: Maison vide
**En tant que** utilisateur
**Je veux** voir un état vide pour une maison sans appareil
**Afin de** comprendre comment ajouter des appareils

**Critères d'acceptation:**
- Message explicatif
- Bouton pour ajouter un appareil

---

### US-022: Ajouter un appareil
**En tant que** utilisateur
**Je veux** ajouter un appareil à ma maison
**Afin de** suivre son entretien

**Critères d'acceptation:**
- Formulaire avec nom, type, marque, modèle, date installation
- Type sélectionnable (Chauffage, Climatisation, Électroménager, etc.)
- L'appareil apparaît dans la liste après création

---

### US-023: Modifier une maison
**En tant que** utilisateur
**Je veux** modifier les informations d'une maison
**Afin de** corriger ou mettre à jour les données

**Critères d'acceptation:**
- Bouton modifier accessible
- Formulaire pré-rempli
- Sauvegarde des modifications

---

### US-024: Supprimer une maison
**En tant que** utilisateur
**Je veux** supprimer une maison
**Afin de** retirer une propriété que je ne gère plus

**Critères d'acceptation:**
- Confirmation avant suppression
- Suppression en cascade (appareils, types, instances)
- Redirection vers dashboard

---

## MVP - Appareil

### US-030: Voir détail appareil
**En tant que** utilisateur
**Je veux** voir le détail d'un appareil
**Afin de** gérer ses entretiens

**Critères d'acceptation:**
- Nom, marque, modèle, date installation
- Badge indiquant le nombre d'entretiens à faire
- Liste des types d'entretien avec statut
- Historique des entretiens (timeline)

---

### US-031: Appareil sans entretien configuré
**En tant que** utilisateur
**Je veux** voir un état vide pour un appareil sans type d'entretien
**Afin de** comprendre comment configurer les entretiens

**Critères d'acceptation:**
- Message explicatif
- Bouton pour ajouter un type d'entretien

---

### US-032: Ajouter un type d'entretien
**En tant que** utilisateur
**Je veux** définir un type d'entretien récurrent
**Afin de** suivre les maintenances périodiques

**Critères d'acceptation:**
- Modal avec formulaire
- Champs: nom (obligatoire), périodicité
- Périodicités: Annuel, Semestriel, Trimestriel, Mensuel
- Le type apparaît dans la liste après création

---

### US-033: Logger un entretien
**En tant que** utilisateur
**Je veux** enregistrer qu'un entretien a été effectué
**Afin de** mettre à jour le suivi

**Critères d'acceptation:**
- Modal avec formulaire (mode rapide et mode détaillé)
- Champs: type (pré-sélectionné), date (obligatoire), coût, prestataire, notes
- L'entretien apparaît dans l'historique immédiatement
- Le statut du type passe à "À jour"
- Calcul automatique de la prochaine échéance
- Rafraîchissement automatique des scores après enregistrement

---

### US-034: Voir historique des entretiens
**En tant que** utilisateur
**Je veux** voir l'historique de tous les entretiens d'un appareil
**Afin de** avoir une traçabilité complète

**Critères d'acceptation:**
- Timeline chronologique (plus récent en haut)
- Pour chaque entrée: type, date, prestataire, coût, notes
- Total des dépenses affiché dans la carte statistiques
- Nombre total d'entretiens loggés affiché

---

### US-035: Modifier un appareil
**En tant que** utilisateur
**Je veux** modifier les informations d'un appareil
**Afin de** corriger ou mettre à jour les données

**Critères d'acceptation:**
- Bouton modifier accessible
- Formulaire pré-rempli
- Sauvegarde des modifications

---

### US-036: Supprimer un appareil
**En tant que** utilisateur
**Je veux** supprimer un appareil
**Afin de** retirer un équipement que je ne possède plus

**Critères d'acceptation:**
- Confirmation avant suppression
- Suppression en cascade (types, instances)
- Redirection vers la maison

---

## MVP - Calculs et Affichage

### US-040: Calcul du score d'un appareil
**En tant que** système
**Je veux** calculer le pourcentage d'entretiens à jour d'un appareil
**Afin de** afficher la progression

**Règle de calcul:**
```
Score = (Types à jour / Total types) × 100
```

**Statut d'un type:**
- **À jour**: dernier entretien + périodicité > aujourd'hui
- **À faire**: dernier entretien + périodicité ≤ aujourd'hui + 30 jours
- **En retard**: dernier entretien + périodicité < aujourd'hui

---

### US-041: Calcul du score d'une maison
**En tant que** système
**Je veux** calculer le score global d'une maison
**Afin de** afficher la progression

**Règle de calcul:**
```
Score = (Total types à jour de tous appareils / Total types de tous appareils) × 100
```

---

### US-042: Calcul du score global
**En tant que** système
**Je veux** calculer le score global de l'utilisateur
**Afin de** afficher sur le dashboard

**Règle de calcul:**
```
Score = Moyenne des scores de toutes les maisons
```

---

## MVP - Dashboard avancé

### US-045: Prochaines tâches
**En tant que** utilisateur
**Je veux** voir les prochaines tâches de maintenance à effectuer
**Afin de** anticiper les entretiens à venir

**Critères d'acceptation:**
- Section "Prochaines tâches" sur le dashboard
- Affiche les 5 prochaines tâches triées par date d'échéance
- Pour chaque tâche: nom, appareil, maison, échéance, statut
- Tâches jamais effectuées affichées en premier
- Code couleur selon statut (rouge=retard, orange=bientôt, vert=ok)

---

## MVP - Personnalisation

### US-051: Thème clair/sombre
**En tant que** utilisateur
**Je veux** basculer entre le thème clair et sombre
**Afin de** adapter l'interface à mes préférences visuelles

**Critères d'acceptation:**
- Bouton toggle dans le header
- Trois options: Clair, Sombre, Système
- Le thème est persisté entre les sessions
- Toutes les pages s'adaptent correctement

---

## MVP - Internationalisation

### US-050: Changer de langue
**En tant que** utilisateur
**Je veux** basculer entre français et anglais
**Afin de** utiliser l'app dans ma langue

**Critères d'acceptation:**
- Toggle FR/EN visible dans le header
- Changement immédiat de la langue
- Préférence sauvegardée

---

## MVP - Infrastructure & Déploiement

### US-060: Déploiement CI/CD avec preprod et prod
**En tant que** développeur
**Je veux** un pipeline CI/CD automatisé avec environnement de preprod et prod
**Afin de** déployer en confiance avec validation avant production

**Critères d'acceptation:**
- Build et push des images Docker sur GHCR avec tag CalVer (YYYY.MM.DD)
- Déploiement automatique en preprod à chaque push sur main
- Possibilité de déployer en preprod depuis n'importe quelle branche (workflow_dispatch)
- Copie de la DB prod vers preprod avant chaque déploiement preprod
- Déploiement en prod uniquement après approval GitHub (environment protection rules)
- Health checks après chaque déploiement
- Script de setup VM pour configurer l'infrastructure depuis zéro
- Backups quotidiens de la DB prod avec rétention 7 jours

---

### US-061: Hardening sécurité infrastructure
**En tant que** développeur
**Je veux** durcir la sécurité de l'infrastructure et du code
**Afin de** réduire la surface d'attaque en production

**Critères d'acceptation:**
- Corriger les injections de script dans le CI
- Ports applicatifs bindés sur 127.0.0.1 uniquement
- Isolation réseau entre containers (network internal + proxy)
- Containers avec `no-new-privileges` et limites CPU/RAM
- Frontend en user non-root dans le Dockerfile
- Secrets générés aléatoirement dans le setup VM
- Scripts avec `set -euo pipefail`, temp files sécurisés, validation des dumps
- Pin GitHub Actions sur SHA
- Migrations DB séparées du démarrage API
- CSP durcie (nonces, suppression unsafe-eval)
- Sanitisation PII dans le sync prod → preprod
- Chiffrement des backups

---

## Phase 2 - Invitations

### US-100: Créer une invitation par lien
**En tant que** propriétaire d'une maison
**Je veux** générer un lien d'invitation pour un rôle donné
**Afin de** inviter quelqu'un à collaborer sur ma maison

**Critères d'acceptation:**
- Générer un lien unique avec token UUID
- Choisir le rôle : Collaborateur RW, Collaborateur RO, ou Locataire
- Le lien expire après 7 jours
- Le lien est copiable en un clic
- Seul le propriétaire peut inviter un collaborateur (RW ou RO)
- Un collaborateur RW peut inviter uniquement un locataire

---

### US-101: Accepter une invitation (utilisateur existant)
**En tant que** utilisateur avec un compte
**Je veux** accepter une invitation via un lien
**Afin de** rejoindre une maison

**Critères d'acceptation:**
- Cliquer sur le lien montre un résumé (nom de la maison, rôle proposé)
- Bouton "Accepter" pour rejoindre la maison
- Après acceptation, la maison apparaît dans le dashboard
- Le lien ne peut être utilisé qu'une seule fois
- Un lien expiré ou révoqué affiche un message d'erreur

---

### US-102: Accepter une invitation (nouvel utilisateur)
**En tant que** personne sans compte
**Je veux** créer un compte à partir d'un lien d'invitation
**Afin de** rejoindre une maison dès mon inscription

**Critères d'acceptation:**
- Le lien redirige vers la page d'inscription avec le token en paramètre
- Après inscription, l'utilisateur est automatiquement ajouté à la maison avec le rôle de l'invitation
- Pas besoin d'action supplémentaire après la création du compte

---

### US-103: Révoquer une invitation
**En tant que** propriétaire ou créateur de l'invitation
**Je veux** révoquer une invitation en cours
**Afin de** annuler un accès non encore accepté

**Critères d'acceptation:**
- Bouton de révocation sur les invitations en attente
- Le lien devient invalide immédiatement
- Le créateur de l'invitation ou le propriétaire peut révoquer

---

## Phase 2 - Rôles et permissions

### US-110: Matrice de permissions par rôle
**En tant que** système
**Je veux** appliquer une matrice de permissions selon le rôle du membre
**Afin de** contrôler les accès sur chaque maison

**Matrice:**

| Action | Owner | Collaborator RW | Collaborator RO | Tenant |
|--------|:---:|:---:|:---:|:---:|
| Voir maison / appareils | ✅ | ✅ | ✅ | ✅ |
| Voir coûts / prestataires | ✅ | ✅ | ✅ | ❌ |
| Logger un entretien | ✅ | ✅ | ❌ | ⚙️ canLogMaintenance |
| Créer type d'entretien | ✅ | ✅ | ❌ | ❌ |
| CRUD appareil | ✅ | ✅ | ❌ | ❌ |
| Modifier / supprimer maison | ✅ | ❌ | ❌ | ❌ |
| Inviter collaborateur | ✅ | ❌ | ❌ | ❌ |
| Inviter locataire | ✅ | ✅ | ❌ | ❌ |
| Gérer permissions membres | ✅ | ❌ | ❌ | ❌ |
| Retirer un membre | ✅ | ❌ | ❌ | ❌ |

**Critères d'acceptation:**
- Chaque endpoint API vérifie le rôle du membre avant d'autoriser l'action
- Les endpoints existants (CRUD maisons, appareils, entretiens) intègrent la vérification
- Un utilisateur non-membre reçoit 403 Forbidden
- Les réponses API masquent les champs coûts/prestataire pour les locataires

---

### US-111: Configurer les permissions d'un locataire
**En tant que** propriétaire
**Je veux** restreindre un locataire en lecture seule
**Afin de** contrôler ce qu'il peut faire

**Critères d'acceptation:**
- Toggle "Peut logger des entretiens" par locataire (activé par défaut)
- Seul le propriétaire peut modifier ce paramètre
- Le changement prend effet immédiatement

---

## Phase 2 - Gestion des membres

### US-120: Page de gestion des collaborateurs (propriétaire)
**En tant que** propriétaire
**Je veux** voir et gérer tous mes collaborateurs sur toutes mes maisons
**Afin de** avoir une vue centralisée de mes partages

**Critères d'acceptation:**
- Page accessible depuis le profil / menu utilisateur
- Liste de tous les membres (collaborateurs) groupés par maison
- Pour chaque membre: nom, email, rôle, date d'ajout
- Actions: modifier le rôle, retirer le membre
- Seul le propriétaire voit cette page

---

### US-121: Page de gestion des locataires (par maison)
**En tant que** propriétaire ou collaborateur RW
**Je veux** voir et gérer les locataires d'une maison
**Afin de** contrôler les accès locataires

**Critères d'acceptation:**
- Section ou onglet dans la page détail de la maison
- Liste des locataires avec nom, email, permissions
- Toggle "Peut logger des entretiens" par locataire
- Bouton pour inviter un nouveau locataire
- Bouton pour retirer un locataire
- Le propriétaire et les collaborateurs RW voient cette section

---

### US-122: Retirer un membre d'une maison
**En tant que** propriétaire
**Je veux** retirer un collaborateur ou locataire d'une maison
**Afin de** révoquer son accès

**Critères d'acceptation:**
- Confirmation avant retrait
- Le membre perd immédiatement l'accès à la maison
- La maison disparaît de son dashboard
- Seul le propriétaire peut retirer un membre

---

### US-123: Modifier le rôle d'un membre
**En tant que** propriétaire
**Je veux** changer le rôle d'un membre
**Afin de** ajuster ses permissions

**Critères d'acceptation:**
- Possibilité de changer entre Collaborateur RW, Collaborateur RO, et Locataire
- Le changement prend effet immédiatement
- Seul le propriétaire peut modifier les rôles
- On ne peut pas changer le rôle du propriétaire

---

## Phase 2 - Dashboard collaborateur/locataire

### US-130: Dashboard avec maisons partagées
**En tant que** collaborateur ou locataire
**Je veux** voir les maisons auxquelles j'ai accès dans mon dashboard
**Afin de** naviguer vers mes maisons partagées

**Critères d'acceptation:**
- Le dashboard affiche les maisons possédées ET les maisons partagées
- Distinction visuelle entre maisons possédées et partagées (badge ou icône)
- Les scores sont calculés de la même manière pour tous
- Un locataire ne voit pas les coûts dans les statistiques et entretiens
- Les "prochaines tâches" incluent les tâches des maisons partagées

---

### US-062: Déploiement Azure avec Workload Identity Federation
**En tant que** développeur
**Je veux** déployer HouseFlow sur Azure Container Apps avec authentification OIDC (Workload Identity Federation)
**Afin de** avoir un déploiement cloud managé, sécurisé et sans secrets Azure stockés dans GitHub

**Contexte technique:**
- Remplace le déploiement VM actuel (SSH + Docker Compose)
- Utilise OIDC entre GitHub Actions et Azure AD (pas de client secret)
- Infrastructure provisionnée via Terraform
- Images Docker stockées sur GHCR (GitHub Container Registry) — pas d'ACR pour réduire les coûts
- PAT GitHub `read:packages` (fine-grained) pour le pull GHCR depuis Azure Container Apps

**Critères d'acceptation:**
- [ ] Terraform pour provisionner l'infrastructure Azure (Resource Group, Container Apps Environment, PostgreSQL Flexible Server)
- [ ] Configuration Workload Identity Federation (Azure AD App Registration + Federated Credential pour GitHub Actions)
- [ ] PAT fine-grained `read:packages` pour le pull GHCR depuis Azure (stocké comme secret Terraform/Azure)
- [ ] Workflow GitHub Actions pour build & push des images vers GHCR (via GITHUB_TOKEN)
- [ ] Déploiement automatique en preprod (Azure Container Apps) à chaque push sur main
- [ ] Déploiement en prod uniquement après approval GitHub (environment protection rules)
- [ ] Health checks après chaque déploiement
- [ ] Variables d'environnement et secrets gérés via Azure Container Apps secrets
- [ ] Configuration réseau : PostgreSQL accessible uniquement depuis les Container Apps (VNet ou firewall rules)
- [ ] Migration DB exécutée comme job séparé avant le déploiement de l'API
- [ ] Documentation du setup initial (bootstrap Terraform + configuration Azure AD)
- [ ] Suppression de l'ancien workflow de déploiement VM (SSH)

---

## Résumé

| Module | Stories | Phase |
|--------|---------|-------|
| Auth | US-001, US-002, US-003 | MVP |
| Dashboard | US-010, US-011, US-012 | MVP |
| Maison | US-020 à US-024 | MVP |
| Appareil | US-030 à US-036 | MVP |
| Calculs | US-040, US-041, US-042 | MVP |
| Dashboard avancé | US-045 | MVP |
| Personnalisation | US-051 | MVP |
| i18n | US-050 | MVP |
| Infrastructure | US-060, US-061 | MVP |
| Azure Deployment | US-062 | MVP |
| Invitations | US-100 à US-103 | Phase 2 |
| Rôles & permissions | US-110, US-111 | Phase 2 |
| Gestion membres | US-120 à US-123 | Phase 2 |
| Dashboard partagé | US-130 | Phase 2 |

**Total: 33 user stories (24 MVP + 9 Phase 2)**

---

**Dernière mise à jour:** 2026-03-17
