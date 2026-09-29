# House Flow - Cahier des Charges

## Vision

**House Flow** est une application web permettant de suivre l'entretien de ses maisons et équipements.

**Domaine cible** : `flow.house`

---

## MVP

### Utilisateurs

- Inscription par email + mot de passe
- Connexion / déconnexion
- Un utilisateur possède ses maisons (le partage fait l'objet de la Phase 2)
- Onboarding guidé après l'inscription, chaque étape pouvant être passée :
  1. **Maison** : nom (pré-rempli « Ma maison ») et adresse facultative. L'inscription ne crée
     aucune maison d'office : la première maison naît ici ;
  2. **Équipements** : catalogue d'équipements à cocher ; pour chacun, le nom de l'entretien
     (pré-rempli, modifiable, 100 caractères max), sa fréquence (3 mois / 6 mois / 1 an / 2 ans)
     et la date du dernier entretien (mois + année, « Plus ancien » ou « Je ne sais pas »), avec
     l'échéancier qui en découle affiché en direct.

### Fonctionnalités

| Entité | Actions |
|--------|---------|
| **Maison** | Créer, modifier, supprimer (illimitées) ; chaque maison a une couleur |
| **Appareil** | Créer, modifier, supprimer par maison |
| **Type d'entretien** | Définir les entretiens récurrents par appareil |
| **Instance d'entretien** | Logger les entretiens réalisés |

### Modèle de données

```
User
 └── House (nom, adresse, couleur)
      └── Device (nom, type, marque, modèle, date installation)
           └── MaintenanceType (nom, périodicité)
                └── MaintenanceInstance (date, coût, prestataire, notes, statut)
```

### Couleur de maison

Pas de photo de maison : chaque maison reçoit à sa création une **couleur** parmi 6 (indigo,
orange, vert, bleu ciel, jaune, rose), attribuée en rotation parmi les maisons de son
propriétaire (la moins utilisée d'abord), et modifiable ensuite par le propriétaire. Elle
identifie la maison partout où elle apparaît : cartes, page de la maison, invitation.

### Types d'appareils (catalogue)

Chaque type du catalogue apporte son entretien par défaut et sa périodicité :

| Type | Entretien par défaut | Périodicité |
|------|---------------------|-------------|
| Chaudière gaz | Entretien annuel | 1 an |
| Détecteur de fumée | Test | 1 an |
| Poêle à bois | Ramonage | 1 an |
| VMC | Nettoyage des bouches | 6 mois |
| Pompe à chaleur | Entretien | 2 ans |
| Chauffe-eau | Détartrage | 2 ans |
| Autre | — (aucun entretien créé) | — |

Chaque type a sa teinte et son icône, reprises partout où l'appareil apparaît.

### Périodicités

- Mensuel, Trimestriel, Semestriel, Annuel, Tous les 2 ans, Personnalisé (« tous les n mois / ans »)

### Statuts d'entretien

- **En retard** : échéance dépassée
- **À faire** : échéance dans les 30 jours
- **À jour** : échéance au-delà de 30 jours

Le statut d'un appareil ou d'une maison est celui de son entretien le plus urgent. Le jour de
référence est celui de Europe/Paris.

### Interface

- Web responsive : mobile, tablette et desktop (points de rupture 640 et 1024 px) ; barre
  d'onglets en bas d'écran sur mobile
- Français et Anglais (i18n)
- Thème **Clair / Sombre / Système** (Système par défaut), choisi dans le compte et appliqué dès le
  premier affichage
- L'icône de l'app (logo du header et favicon) prend la couleur du statut global — le plus
  urgent de toutes les maisons visibles ; icône neutre tant que ce statut n'est pas connu
- Polices servies par l'application elle-même : aucune ressource chargée depuis un tiers
- Le rendu attendu de chaque écran (pages, popups, tokens, états) est décrit par
  [`specs/ux/`](ux/README.md), référence visuelle unique

### Sécurité

- Mot de passe : 8+ caractères, majuscule, minuscule, chiffre, caractère spécial
- Token JWT stocké en localStorage (persistance après refresh)
- Refresh token en cookie HttpOnly

### UX

- Accueil centré sur « ce qu'il y a à faire » : tous les entretiens en retard et à faire, toutes
  maisons confondues, avec « C'est fait » en un clic (annulable)
- Appareils triés par priorité : en retard → à faire → à jour
- Historique des entretiens trié par date (plus récent en haut)
- Fil d'Ariane de navigation (lien « ‹ Parent » sur mobile)
- Squelettes pendant les chargements ; aucune valeur par défaut affichée avant la réponse

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

- Invitation par **email + rôle**, matérialisée par un **lien partageable** : aucun email n'est
  envoyé, l'invitant copie le lien
- Si la personne a déjà un compte → elle accepte l'invitation et est ajoutée à la maison
- Si la personne n'a pas de compte → le lien la redirige vers la création de compte, puis l'ajoute automatiquement à la maison
- Une invitation a une durée de validité (7 jours par défaut)
- Une invitation peut être renvoyée (nouveau lien) ou annulée par le propriétaire ; un
  collaborateur RW peut faire de même pour les invitations de locataire
- La personne invitée peut accepter ou refuser l'invitation

### Rôles et permissions

Chaque maison a un **propriétaire** (le créateur) et peut avoir des **membres** : collaborateur
en lecture/écriture, collaborateur en lecture seule, ou locataire.

La matrice détaillée des permissions fait partie de l'architecture et vit dans
[`architecture.md`](architecture.md) § *Modèle d'autorisation (RBAC)*, au plus près de son
implémentation et de ses tests. Elle n'est pas recopiée ici.

**Collaborateur RW et locataires** : un collaborateur en lecture/écriture peut inviter un
locataire — et seulement un locataire ; tout le reste de la gestion des membres (inviter un
collaborateur, changer un rôle, retirer un membre) reste réservé au propriétaire.

**Locataire configurable** : par défaut, un locataire peut logger un entretien. Le propriétaire
peut l'en priver (`canLogMaintenance`).

### Modèle de données

```
User
 └── House (propriétaire = User.Id)
      ├── HouseMember (userId, houseId, role, canLogMaintenance)
      │    role: Owner | CollaboratorRW | CollaboratorRO | Tenant
      └── Invitation (houseId, email, role, token, expiresAt, createdByUserId, status)
           status: Pending | Accepted | Expired | Revoked | Declined
```

- `HouseMember` : table de jointure entre User et House, avec le rôle
- Le propriétaire a toujours un enregistrement HouseMember avec role=Owner
- `canLogMaintenance` : booléen, uniquement pertinent pour les locataires (permet de restreindre)
- `Invitation` : contient un token unique (UUID) utilisé dans le lien d'invitation

### Interface

- **Gestion des membres** : depuis la page d'une maison (fenêtre « Membres ») — membres, rôles,
  invitations en attente. Un collaborateur RW l'ouvre en mode restreint (membres en lecture seule,
  invitation de locataires uniquement)
- Les cartes de maison signalent une maison **partagée avec moi** (dont je ne suis pas propriétaire)
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
