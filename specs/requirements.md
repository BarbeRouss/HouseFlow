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

## Hors scope MVP

- Paiement / abonnement
- Partage / collaboration
- Emails / notifications
- Upload fichiers
- Application mobile native

---

## Phase 2 : Collaboration

### Système d'invitation

- Invitation par **lien partageable** (pas d'envoi d'email automatique pour l'instant)
- Si la personne a déjà un compte → elle accepte l'invitation et est ajoutée à la maison
- Si la personne n'a pas de compte → le lien la redirige vers la création de compte, puis l'ajoute automatiquement à la maison
- Une invitation a une durée de validité (7 jours par défaut)
- Une invitation peut être révoquée par son créateur ou le propriétaire

### Rôles et permissions

Chaque maison a un **propriétaire** (le créateur) et peut avoir des **membres** : collaborateur
en lecture/écriture, collaborateur en lecture seule, ou locataire.

La matrice détaillée des permissions fait partie de l'architecture et vit dans
[`architecture.md`](architecture.md) § *Modèle d'autorisation (RBAC)*, au plus près de son
implémentation et de ses tests. Elle n'est pas recopiée ici.

**Locataire configurable** : par défaut, un locataire peut logger un entretien. Le propriétaire ou
un collaborateur RW peut l'en priver (`canLogMaintenance`).

### Modèle de données

```
User
 └── House (propriétaire = User.Id)
      ├── HouseMember (userId, houseId, role, canLogMaintenance)
      │    role: Owner | CollaboratorRW | CollaboratorRO | Tenant
      └── Invitation (houseId, role, token, expiresAt, createdByUserId, status)
           status: Pending | Accepted | Expired | Revoked
```

- `HouseMember` : table de jointure entre User et House, avec le rôle
- Le propriétaire a toujours un enregistrement HouseMember avec role=Owner
- `canLogMaintenance` : booléen, uniquement pertinent pour les locataires (permet de restreindre)
- `Invitation` : contient un token unique (UUID) utilisé dans le lien d'invitation

### Interface

- **Page de gestion des collaborateurs** : accessible depuis le profil du propriétaire, vue transversale de tous les collaborateurs sur toutes ses maisons
- **Page de gestion des locataires** : accessible depuis le détail d'une maison, gestion des locataires de cette maison spécifique
- Le dashboard des collaborateurs/locataires montre les mêmes scores que le propriétaire (sauf coûts masqués pour les locataires)
- Pas de notification in-app pour les invitations

### Hors scope Phase 2

- Envoi d'email automatique (les liens sont partagés manuellement)
- Notifications in-app
- Transfert de propriété d'une maison
- Rôles personnalisés

---

## Au-delà de la Phase 2

Le backlog ne vit pas dans ce fichier : il vit dans les
[issues GitHub](https://github.com/BarbeRouss/HouseFlow/issues). Les lots qui figuraient ici
(notifications, premium/Stripe, enrichissement) y ont chacun leurs issues, ouvertes ou fermées
selon ce qui a été décidé.

Ce document décrit le **périmètre produit** : ce que l'application est censée faire et ce qu'elle
a explicitement choisi de ne pas faire. Pas son avancement.
