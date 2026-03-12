# User Stories - House Flow MVP

## Authentification

### US-001: Inscription
**En tant que** visiteur
**Je veux** créer un compte avec mon email et mot de passe
**Afin de** pouvoir utiliser l'application

**Critères d'acceptation:**
- [ ] Formulaire avec prénom, nom, email, mot de passe
- [ ] Validation email unique
- [ ] Mot de passe min 8 caractères
- [ ] Redirection vers dashboard après inscription
- [ ] Création automatique d'une première maison "Ma maison"

**Wireframe:** `register.html`

---

### US-002: Connexion
**En tant que** utilisateur inscrit
**Je veux** me connecter avec mon email et mot de passe
**Afin de** accéder à mes données

**Critères d'acceptation:**
- [ ] Formulaire email + mot de passe
- [ ] Message d'erreur si identifiants incorrects
- [ ] Redirection vers dashboard après connexion
- [ ] Token JWT stocké pour les requêtes

**Wireframe:** `login.html`

---

### US-003: Déconnexion
**En tant que** utilisateur connecté
**Je veux** me déconnecter
**Afin de** sécuriser mon compte

**Critères d'acceptation:**
- [ ] Bouton de déconnexion accessible
- [ ] Suppression du token
- [ ] Redirection vers page de connexion

---

## Dashboard

### US-010: Voir mes maisons
**En tant que** utilisateur connecté
**Je veux** voir la liste de toutes mes maisons
**Afin de** avoir une vue d'ensemble

**Critères d'acceptation:**
- [ ] Liste des maisons avec nom et adresse
- [ ] Score de progression par maison (%)
- [ ] Badge "Parfait" si 100%
- [ ] Badge "X à faire" si entretiens en attente
- [ ] Score global affiché (moyenne)

**Wireframe:** `dashboard.html`

---

### US-011: Dashboard vide
**En tant que** nouvel utilisateur
**Je veux** voir un état vide accueillant
**Afin de** comprendre comment démarrer

**Critères d'acceptation:**
- [ ] Message explicatif
- [ ] Bouton d'action pour ajouter une maison

**Wireframe:** `dashboard-empty.html`

---

### US-012: Ajouter une maison
**En tant que** utilisateur
**Je veux** ajouter une nouvelle maison
**Afin de** gérer plusieurs propriétés

**Critères d'acceptation:**
- [ ] Modal avec formulaire
- [ ] Champs: nom (obligatoire), adresse, code postal, ville
- [ ] La maison apparaît dans la liste après création

**Wireframe:** `dashboard.html` (modal)

---

## Maison

### US-020: Voir détail maison
**En tant que** utilisateur
**Je veux** voir le détail d'une maison
**Afin de** gérer ses appareils

**Critères d'acceptation:**
- [ ] Nom et adresse de la maison
- [ ] Score de la maison (%)
- [ ] Liste des appareils avec leur progression
- [ ] Badge statut par appareil (À jour / À faire / En retard)

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
- [ ] Modal avec formulaire
- [ ] Champs: type (pré-sélectionné), date (obligatoire), coût, prestataire, notes
- [ ] L'entretien apparaît dans l'historique
- [ ] Le statut du type passe à "À jour"
- [ ] Calcul automatique de la prochaine échéance

**Wireframe:** `device.html` (modal "Logger un entretien")

---

### US-034: Voir historique des entretiens
**En tant que** utilisateur
**Je veux** voir l'historique de tous les entretiens d'un appareil
**Afin de** avoir une traçabilité complète

**Critères d'acceptation:**
- [ ] Timeline chronologique (plus récent en haut)
- [ ] Pour chaque entrée: type, date, prestataire, coût
- [ ] Total des dépenses affiché

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

| Module | Stories | Priorité |
|--------|---------|----------|
| Auth | US-001, US-002, US-003 | P0 |
| Dashboard | US-010, US-011, US-012 | P0 |
| Maison | US-020, US-021, US-022, US-023, US-024 | P0 |
| Appareil | US-030, US-031, US-032, US-033, US-034, US-035, US-036 | P0 |
| Calculs | US-040, US-041, US-042 | P0 |
| i18n | US-050 | P1 |

**Total: 20 user stories**
