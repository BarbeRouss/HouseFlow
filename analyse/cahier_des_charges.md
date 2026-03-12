# House Flow - Cahier des Charges

## Vision

**House Flow** est une application web permettant de suivre l'entretien de ses maisons et équipements.

**Domaine cible** : `flow.house`

---

## MVP

### Utilisateurs

- Inscription par email + mot de passe
- Connexion / déconnexion
- Un utilisateur possède ses maisons (pas de partage)

### Fonctionnalités

| Entité | Actions |
|--------|---------|
| **Maison** | Créer, modifier, supprimer (illimitées) |
| **Appareil** | Créer, modifier, supprimer par maison |
| **Type d'entretien** | Définir les entretiens récurrents par appareil |
| **Instance d'entretien** | Logger les entretiens réalisés |

### Modèle de données

```
User
 └── House (nom, adresse)
      └── Device (nom, type, marque, modèle, date installation)
           └── MaintenanceType (nom, périodicité)
                └── MaintenanceInstance (date, coût, prestataire, notes, statut)
```

### Types d'appareils (catalogue)

- Chauffage : Chaudière, Poêle à bois/pellet, Pompe à chaleur
- Sécurité : Alarme incendie, Détecteur CO, Extincteur
- Plomberie : Chauffe-eau, Adoucisseur
- Électroménager : Climatisation, VMC
- Autre (champ libre)

### Périodicités

- Annuel, Semestriel, Trimestriel, Mensuel, Personnalisé (X jours)

### Statuts d'entretien

- Planifié, Réalisé, En retard

### Interface

- Web responsive (desktop + mobile)
- Français et Anglais (i18n)
- Design moderne et simple

### Sécurité

- Mot de passe : 12+ caractères, majuscule, minuscule, chiffre, caractère spécial
- Token JWT stocké en localStorage (persistance après refresh)
- Refresh token en cookie HttpOnly

### UX

- Appareils triés par priorité : en retard → en attente → à jour
- Historique des entretiens trié par date (plus récent en haut)
- Breadcrumb de navigation
- Skeleton loading pour les chargements

---

## Roadmap (post-MVP)

### Phase 2 : Collaboration

- Inviter des collaborateurs sur une maison
- Permissions : lecture seule ou lecture/écriture
- Accès locataire (vue limitée sans coûts)

### Phase 3 : Notifications

- Rappels par email (X jours avant échéance)
- Configuration des préférences de notification

### Phase 4 : Premium

- Organisation (niveau entreprise)
- Abonnement Stripe
- Fonctionnalités avancées (stats, exports, documents)

### Phase 5 : Enrichissement

- Upload photos/documents (factures, certificats)
- Statistiques et budgets
- Suggestions légales par pays/type d'appareil

---

## Hors scope MVP

- Paiement / abonnement
- Partage / collaboration
- Emails / notifications
- Upload fichiers
- Application mobile native

---

**Version** : 2.1
**Date** : 2026-03-12
**Statut** : MVP implémenté (108 tests passants)
