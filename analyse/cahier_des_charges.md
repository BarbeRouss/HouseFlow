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

**Version** : 2.0
**Date** : 2025-03-11
**Statut** : MVP défini
