# User Stories - House Flow MVP

## Authentification

### US-001: Inscription
**En tant que** visiteur
**Je veux** créer un compte avec mon email et mot de passe
**Afin de** pouvoir utiliser l'application

**Critères d'acceptation:**
- [x] Formulaire avec prénom, nom, email, mot de passe
- [x] Validation email unique
- [x] Mot de passe min 12 caractères avec majuscule, minuscule, chiffre et caractère spécial
- [x] Redirection vers page d'ajout d'appareil après inscription
- [x] Création automatique d'une première maison "Ma maison"

**Wireframe:** `register.html`

---

### US-002: Connexion
**En tant que** utilisateur inscrit
**Je veux** me connecter avec mon email et mot de passe
**Afin de** accéder à mes données

**Critères d'acceptation:**
- [x] Formulaire email + mot de passe
- [x] Message d'erreur si identifiants incorrects
- [x] Redirection vers dashboard après connexion
- [x] Token JWT stocké en localStorage (persistance après refresh de page)
- [x] Refresh token en cookie HttpOnly pour renouvellement automatique

**Wireframe:** `login.html`

---

### US-003: Déconnexion
**En tant que** utilisateur connecté
**Je veux** me déconnecter
**Afin de** sécuriser mon compte

**Critères d'acceptation:**
- [x] Bouton de déconnexion accessible
- [x] Suppression du token (localStorage et cookie)
- [x] Redirection vers page de connexion

---

## Dashboard

### US-010: Voir mes maisons
**En tant que** utilisateur connecté
**Je veux** voir la liste de toutes mes maisons
**Afin de** avoir une vue d'ensemble

**Critères d'acceptation:**
- [x] Liste des maisons avec nom et adresse
- [x] Score de progression par maison (%) avec cercle de score visuel
- [x] Badge "OK" si 100%
- [x] Badge "X en attente" et "X en retard" si entretiens non à jour
- [x] Score global affiché (moyenne pondérée)

**Wireframe:** `dashboard.html`

---

### US-011: Dashboard vide
**En tant que** nouvel utilisateur
**Je veux** voir un état vide accueillant
**Afin de** comprendre comment démarrer

**Critères d'acceptation:**
- [x] Message explicatif
- [x] Bouton d'action pour ajouter une maison

**Wireframe:** `dashboard-empty.html`

---

### US-012: Ajouter une maison
**En tant que** utilisateur
**Je veux** ajouter une nouvelle maison
**Afin de** gérer plusieurs propriétés

**Critères d'acceptation:**
- [x] Page dédiée avec formulaire
- [x] Champs: nom (obligatoire), adresse, code postal, ville
- [x] La maison apparaît dans la liste après création

**Wireframe:** `dashboard.html` (modal)

---

## Maison

### US-020: Voir détail maison
**En tant que** utilisateur
**Je veux** voir le détail d'une maison
**Afin de** gérer ses appareils

**Critères d'acceptation:**
- [x] Nom et adresse de la maison
- [x] Score de la maison (%) avec cercle de score visuel
- [x] Liste des appareils triés par priorité (en retard → en attente → à jour)
- [x] Badge statut par appareil (À jour / À faire / En retard)
- [x] Barre de progression par appareil

**Wireframe:** `house.html`

---

### US-021: Maison vide
**En tant que** utilisateur
**Je veux** voir un état vide pour une maison sans appareil
**Afin de** comprendre comment ajouter des appareils

**Critères d'acceptation:**
- [x] Message explicatif
- [x] Bouton pour ajouter un appareil

**Wireframe:** `house-empty.html`

---

### US-022: Ajouter un appareil
**En tant que** utilisateur
**Je veux** ajouter un appareil à ma maison
**Afin de** suivre son entretien

**Critères d'acceptation:**
- [x] Formulaire avec nom, type, marque, modèle, date installation
- [x] Type sélectionnable (Chauffage, Climatisation, Électroménager, etc.)
- [x] L'appareil apparaît dans la liste après création

**Wireframe:** `house.html` (bouton ajouter)

---

### US-023: Modifier une maison
**En tant que** utilisateur
**Je veux** modifier les informations d'une maison
**Afin de** corriger ou mettre à jour les données

**Critères d'acceptation:**
- [x] Bouton modifier accessible
- [x] Formulaire pré-rempli
- [x] Sauvegarde des modifications

---

### US-024: Supprimer une maison
**En tant que** utilisateur
**Je veux** supprimer une maison
**Afin de** retirer une propriété que je ne gère plus

**Critères d'acceptation:**
- [x] Confirmation avant suppression
- [x] Suppression en cascade (appareils, types, instances)
- [x] Redirection vers dashboard

---

## Appareil

### US-030: Voir détail appareil
**En tant que** utilisateur
**Je veux** voir le détail d'un appareil
**Afin de** gérer ses entretiens

**Critères d'acceptation:**
- [x] Nom, marque, modèle, date installation
- [x] Badge indiquant le nombre d'entretiens à faire
- [x] Liste des types d'entretien avec statut
- [x] Historique des entretiens (timeline)

**Wireframe:** `device.html`

---

### US-031: Appareil sans entretien configuré
**En tant que** utilisateur
**Je veux** voir un état vide pour un appareil sans type d'entretien
**Afin de** comprendre comment configurer les entretiens

**Critères d'acceptation:**
- [x] Message explicatif
- [x] Bouton pour ajouter un type d'entretien

**Wireframe:** `device-empty.html`

---

### US-032: Ajouter un type d'entretien
**En tant que** utilisateur
**Je veux** définir un type d'entretien récurrent
**Afin de** suivre les maintenances périodiques

**Critères d'acceptation:**
- [x] Modal avec formulaire
- [x] Champs: nom (obligatoire), périodicité
- [x] Périodicités: Annuel, Semestriel, Trimestriel, Mensuel
- [x] Le type apparaît dans la liste après création

**Wireframe:** `device.html` (modal "Ajouter un type d'entretien")

---

### US-033: Logger un entretien
**En tant que** utilisateur
**Je veux** enregistrer qu'un entretien a été effectué
**Afin de** mettre à jour le suivi

**Critères d'acceptation:**
- [x] Modal avec formulaire (mode rapide et mode détaillé)
- [x] Champs: type (pré-sélectionné), date (obligatoire), coût, prestataire, notes
- [x] L'entretien apparaît dans l'historique immédiatement
- [x] Le statut du type passe à "À jour"
- [x] Calcul automatique de la prochaine échéance
- [x] Rafraîchissement automatique des scores après enregistrement

**Wireframe:** `device.html` (modal "Logger un entretien")

---

### US-034: Voir historique des entretiens
**En tant que** utilisateur
**Je veux** voir l'historique de tous les entretiens d'un appareil
**Afin de** avoir une traçabilité complète

**Critères d'acceptation:**
- [x] Timeline chronologique (plus récent en haut)
- [x] Pour chaque entrée: type, date, prestataire, coût, notes
- [x] Total des dépenses affiché dans la carte statistiques
- [x] Nombre total d'entretiens loggés affiché

**Wireframe:** `device.html` (section historique)

---

### US-035: Modifier un appareil
**En tant que** utilisateur
**Je veux** modifier les informations d'un appareil
**Afin de** corriger ou mettre à jour les données

**Critères d'acceptation:**
- [x] Bouton modifier accessible
- [x] Formulaire pré-rempli
- [x] Sauvegarde des modifications

---

### US-036: Supprimer un appareil
**En tant que** utilisateur
**Je veux** supprimer un appareil
**Afin de** retirer un équipement que je ne possède plus

**Critères d'acceptation:**
- [x] Confirmation avant suppression
- [x] Suppression en cascade (types, instances)
- [x] Redirection vers la maison

---

## Calculs et Affichage

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

## Dashboard avancé

### US-045: Prochaines tâches
**En tant que** utilisateur
**Je veux** voir les prochaines tâches de maintenance à effectuer
**Afin de** anticiper les entretiens à venir

**Critères d'acceptation:**
- [x] Section "Prochaines tâches" sur le dashboard
- [x] Affiche les 5 prochaines tâches triées par date d'échéance
- [x] Pour chaque tâche: nom, appareil, maison, échéance, statut
- [x] Tâches jamais effectuées affichées en premier
- [x] Code couleur selon statut (rouge=retard, orange=bientôt, vert=ok)

**Wireframe:** `dashboard.html` (section prochaines tâches)

---

## Personnalisation

### US-051: Thème clair/sombre
**En tant que** utilisateur
**Je veux** basculer entre le thème clair et sombre
**Afin de** adapter l'interface à mes préférences visuelles

**Critères d'acceptation:**
- [x] Bouton toggle dans le header
- [x] Trois options: Clair, Sombre, Système
- [x] Le thème est persisté entre les sessions
- [x] Toutes les pages s'adaptent correctement

---

## Internationalisation

### US-050: Changer de langue
**En tant que** utilisateur
**Je veux** basculer entre français et anglais
**Afin de** utiliser l'app dans ma langue

**Critères d'acceptation:**
- [x] Toggle FR/EN visible dans le header
- [x] Changement immédiat de la langue
- [ ] Préférence sauvegardée

---

## Infrastructure & Déploiement

### US-060: Déploiement CI/CD avec preprod et prod
**En tant que** développeur
**Je veux** un pipeline CI/CD automatisé avec environnement de preprod et prod
**Afin de** déployer en confiance avec validation avant production

**Critères d'acceptation:**
- [ ] Build et push des images Docker sur GHCR avec tag CalVer (YYYY.MM.DD)
- [ ] Déploiement automatique en preprod à chaque push sur main
- [ ] Possibilité de déployer en preprod depuis n'importe quelle branche (workflow_dispatch)
- [ ] Copie de la DB prod vers preprod avant chaque déploiement preprod
- [ ] Déploiement en prod uniquement après approval GitHub (environment protection rules)
- [ ] Health checks après chaque déploiement
- [ ] Script de setup VM pour configurer l'infrastructure depuis zéro
- [ ] Backups quotidiens de la DB prod avec rétention 7 jours

---

### US-061: Hardening sécurité infrastructure
**En tant que** développeur
**Je veux** durcir la sécurité de l'infrastructure et du code
**Afin de** réduire la surface d'attaque en production

**Critères d'acceptation:**
- [x] Corriger les injections de script dans le CI (`${{ }}` dans les `run:` blocks)
- [x] Ports applicatifs bindés sur 127.0.0.1 uniquement
- [x] Isolation réseau entre containers (network internal + proxy)
- [x] Containers avec `no-new-privileges` et limites CPU/RAM
- [x] Frontend en user non-root dans le Dockerfile
- [x] Secrets générés aléatoirement dans le setup VM
- [x] Scripts avec `set -euo pipefail`, temp files sécurisés, validation des dumps
- [ ] Pin GitHub Actions sur SHA
- [ ] Migrations DB séparées du démarrage API
- [ ] CSP durcie (nonces, suppression unsafe-eval)
- [ ] Sanitisation PII dans le sync prod → preprod
- [ ] Chiffrement des backups

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

**Critères d'acceptation:**
- [ ] Terraform pour provisionner l'infrastructure Azure (Resource Group, Container Apps Environment, PostgreSQL Flexible Server)
- [ ] Configuration Workload Identity Federation (Azure AD App Registration + Federated Credential pour GitHub Actions)
- [ ] Workflow GitHub Actions pour build & push des images vers GHCR (via GITHUB_TOKEN)
- [ ] Configuration du pull credential GHCR dans Azure Container Apps (via Terraform)
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

| Module | Stories | Priorité | Statut |
|--------|---------|----------|--------|
| Auth | US-001, US-002, US-003 | P0 | ✅ Complet |
| Dashboard | US-010, US-011, US-012 | P0 | ✅ Complet |
| Maison | US-020, US-021, US-022, US-023, US-024 | P0 | ⚠️ Partiel (1/5) |
| Appareil | US-030, US-031, US-032, US-033, US-034, US-035, US-036 | P0 | ⚠️ Partiel (2/7) |
| Calculs | US-040, US-041, US-042 | P0 | ✅ Complet |
| Dashboard avancé | US-045 | P1 | ✅ Complet |
| i18n | US-050 | P1 | ⚠️ Partiel (backend ok, UI manquante) |
| Infrastructure | US-060 | P0 | ❌ Non implémenté |
| Sécurité | US-061 | P1 | ⚠️ Partiel (7/12) |
| Azure Deployment | US-062 | P1 | ❌ Non implémenté |

**Total: 24 user stories**

### Détail des US partielles

**Maison (US-020 à US-024):**
- ✅ US-020: Voir détail maison
- ❌ US-021: Empty state maison
- ❌ US-022: Formulaire ajout appareil
- ❌ US-023: Modifier maison
- ❌ US-024: Supprimer maison

**Appareil (US-030 à US-036):**
- ❌ US-030: Page détail appareil
- ❌ US-031: Empty state appareil
- ❌ US-032: Modal ajouter type entretien
- ✅ US-033: Logger un entretien
- ✅ US-034: Voir historique
- ❌ US-035: Modifier appareil
- ❌ US-036: Supprimer appareil

---

## Couverture de tests

| Type | Nombre | Statut |
|------|--------|--------|
| Tests unitaires | 7 | ✅ |
| Tests d'intégration | 78 | ✅ |
| Tests E2E (Playwright) | 23 | ✅ |
| **Total** | **108** | ✅ |

**Dernière mise à jour:** 2026-03-12
