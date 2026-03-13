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
- [ ] Message explicatif
- [ ] Bouton pour ajouter un appareil

**Wireframe:** `house-empty.html`

---

### US-022: Ajouter un appareil
**En tant que** utilisateur
**Je veux** ajouter un appareil à ma maison
**Afin de** suivre son entretien

**Critères d'acceptation:**
- [ ] Formulaire avec nom, type, marque, modèle, date installation
- [ ] Type sélectionnable (Chauffage, Climatisation, Électroménager, etc.)
- [ ] L'appareil apparaît dans la liste après création

**Wireframe:** `house.html` (bouton ajouter)

---

### US-023: Modifier une maison
**En tant que** utilisateur
**Je veux** modifier les informations d'une maison
**Afin de** corriger ou mettre à jour les données

**Critères d'acceptation:**
- [ ] Bouton modifier accessible
- [ ] Formulaire pré-rempli
- [ ] Sauvegarde des modifications

---

### US-024: Supprimer une maison
**En tant que** utilisateur
**Je veux** supprimer une maison
**Afin de** retirer une propriété que je ne gère plus

**Critères d'acceptation:**
- [ ] Confirmation avant suppression
- [ ] Suppression en cascade (appareils, types, instances)
- [ ] Redirection vers dashboard

---

## Appareil

### US-030: Voir détail appareil
**En tant que** utilisateur
**Je veux** voir le détail d'un appareil
**Afin de** gérer ses entretiens

**Critères d'acceptation:**
- [ ] Nom, marque, modèle, date installation
- [ ] Badge indiquant le nombre d'entretiens à faire
- [ ] Liste des types d'entretien avec statut
- [ ] Historique des entretiens (timeline)

**Wireframe:** `device.html`

---

### US-031: Appareil sans entretien configuré
**En tant que** utilisateur
**Je veux** voir un état vide pour un appareil sans type d'entretien
**Afin de** comprendre comment configurer les entretiens

**Critères d'acceptation:**
- [ ] Message explicatif
- [ ] Bouton pour ajouter un type d'entretien

**Wireframe:** `device-empty.html`

---

### US-032: Ajouter un type d'entretien
**En tant que** utilisateur
**Je veux** définir un type d'entretien récurrent
**Afin de** suivre les maintenances périodiques

**Critères d'acceptation:**
- [ ] Modal avec formulaire
- [ ] Champs: nom (obligatoire), périodicité
- [ ] Périodicités: Annuel, Semestriel, Trimestriel, Mensuel
- [ ] Le type apparaît dans la liste après création

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
- [ ] Bouton modifier accessible
- [ ] Formulaire pré-rempli
- [ ] Sauvegarde des modifications

---

### US-036: Supprimer un appareil
**En tant que** utilisateur
**Je veux** supprimer un appareil
**Afin de** retirer un équipement que je ne possède plus

**Critères d'acceptation:**
- [ ] Confirmation avant suppression
- [ ] Suppression en cascade (types, instances)
- [ ] Redirection vers la maison

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
- [ ] Section "Prochaines tâches" sur le dashboard
- [ ] Affiche les 5 prochaines tâches triées par date d'échéance
- [ ] Pour chaque tâche: nom, appareil, maison, échéance, statut
- [ ] Tâches jamais effectuées affichées en premier
- [ ] Code couleur selon statut (rouge=retard, orange=bientôt, vert=ok)

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
- [ ] Toggle FR/EN visible dans le header
- [ ] Changement immédiat de la langue
- [ ] Préférence sauvegardée

---

## Résumé

| Module | Stories | Priorité | Statut |
|--------|---------|----------|--------|
| Auth | US-001, US-002, US-003 | P0 | ✅ Complet |
| Dashboard | US-010, US-011, US-012 | P0 | ✅ Complet |
| Maison | US-020, US-021, US-022, US-023, US-024 | P0 | ⚠️ Partiel (1/5) |
| Appareil | US-030, US-031, US-032, US-033, US-034, US-035, US-036 | P0 | ⚠️ Partiel (2/7) |
| Calculs | US-040, US-041, US-042 | P0 | ✅ Complet |
| Dashboard avancé | US-045 | P1 | ❌ Non implémenté |
| i18n | US-050 | P1 | ⚠️ Partiel (backend ok, UI manquante) |

**Total: 21 user stories**

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
