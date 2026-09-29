# Refonte UX de l'application (spec v2)

> Source de vérité : **`docs/design/spec-refonte-v2.html`** (spec autonome, à ouvrir dans un navigateur). En cas d'écart entre cette issue et la spec, la spec fait foi. Les identifiants P01–P15, M1–M7, C1–C8 et R1–R7 renvoient aux sections de ce fichier.
> Une seule issue / une seule PR, volontairement. On itère dans la PR.
> **Hors périmètre :** email de rappel (N1) et préférences de rappels → issue séparée « Rappels par email ».

## Objectif
- Navigation persistante à 2 entrées : **Accueil · Maisons**.
- « C'est fait » en un clic, sur place.
- Onboarding guidé jusqu'au premier échéancier.
- États de chargement, d'erreur et vides partout. Aucune valeur par défaut affichée avant la réponse du serveur.

---

## 1. Règles produit (à respecter partout)

**R1 · Statut d'un entretien** (fuseau Europe/Paris)
- En retard : `nextDueDate < aujourd'hui`
- À faire : `aujourd'hui ≤ nextDueDate ≤ aujourd'hui + 30 j`
- À jour : `nextDueDate > aujourd'hui + 30 j`
- Statut d'un appareil ou d'une maison = celui de son entretien le plus urgent.
- Toujours afficher pastille + libellé texte, jamais la couleur seule.

**R2 · Échéance suivante**
- Après un enregistrement : `nextDueDate = date de réalisation + périodicité`.
- Dernier entretien saisi à la création (mois + année) : un enregistrement est créé au **1er du mois choisi**, avec la note « Date approximative (mois) ».
- « Je ne sais pas » : aucun enregistrement, `nextDueDate = createdAt + 30 j`.
- « Plus ancien » : aucun enregistrement, `nextDueDate = createdAt`.
- Supprimer le dernier enregistrement recalcule l'échéance depuis le précédent (ou applique la règle « sans historique » s'il n'y en a plus).

**R3 · Compteurs et score**
- « À traiter » = En retard + À faire, sur toutes les maisons visibles. Même nombre dans le titre de l'Accueil et dans le badge de la nav.
- Badge rouge s'il y a au moins 1 entretien en retard, gris sinon, masqué si 0.
- Score affiché sous la forme « {à jour}/{total} à jour ». Pas de pourcentage, de tendance ni de série.

**R4 · Dates**
- Format absolu `d MMM yyyy` localisé. Supprimer `InvariantCulture`.
- Format relatif à 60 jours ou moins (« en retard de 24 j », « aujourd'hui », « dans 12 j »), sinon « mars 2027 ».
- Pluriels gérés.

**R5 · Rôles** : une action non autorisée est **masquée**, jamais désactivée.

| Action | Propriétaire | Collaborateur (RW) | Lecture seule (RO) | Locataire |
|---|---|---|---|---|
| Voir | ✓ | ✓ | ✓ | ✓ |
| C'est fait / enregistrer, modifier un enregistrement | ✓ | ✓ | — | ✓ |
| Ajouter / modifier appareil ou type d'entretien | ✓ | ✓ | — | — |
| Supprimer appareil, type ou enregistrement | ✓ | ✓ | — | — |
| Modifier / supprimer la maison | ✓ | — | — | — |
| Inviter un locataire ; renvoyer / annuler une invitation de locataire | ✓ | ✓ | — | — |
| Gérer membres et invitations (inviter un collaborateur, changer un rôle, retirer un membre) | ✓ | — | — | — |

Le collaborateur RW ouvre M5 en mode restreint : liste des membres en lecture seule, invitation au rôle Locataire uniquement, seules les invitations de locataire sont listées.

**R6 · Navigation**
- Après une création → l'élément créé.
- Après une suppression → le parent, avec le toast « {nom} supprimé ».
- Sans session → `/login?returnUrl=`.
- Utilisateur connecté sur `/`, `/login` ou `/register` → `/dashboard`.
- Modales : Échap, clic sur le fond ou « Annuler » les ferment.

**R7 · Responsive** : point de rupture unique à 640 px. En dessous :
- barre d'onglets en bas (Accueil · Maisons · Compte) ;
- modales en panneaux ancrés en bas d'écran ;
- zones tactiles d'au moins 44 px.

---

## 2. Composants partagés

- [ ] **C1 · Header** (≥ 640 px)
  - Logo → P07, entrées « Accueil » (avec badge R3) et « Maisons ».
  - Menu avatar : Compte, Administration (admins seulement), Se déconnecter.
  - Langue et thème retirés du header, déplacés dans P11.
- [ ] **C2 · Barre d'onglets** (< 640 px) : Accueil · Maisons · Compte, même badge que C1.
- [ ] **C3 · Ligne d'entretien**
  - Bouton **icône Lucide `check` + libellé « C'est fait »**, jamais l'icône seule. Plein si en retard, contour sinon.
  - « C'est fait » **enregistre immédiatement** à la date du jour : la ligne se met à jour sur place, puis toast C5. Bouton désactivé pendant la requête.
  - Menu ⋯ : « Fait à une autre date… » (M3), « Modifier l'entretien » (M4), « Supprimer l'entretien » (M6). Sur P07, seulement « Fait à une autre date… ».
  - Lecture seule : ni bouton ni menu.
  - Clic sur la ligne → P10 (sauf si on est déjà sur P10).
- [ ] **C4 · Ligne maison / appareil**
  - Lien porté par le nom et étendu à la ligne (pas de `<a>` qui englobe tout).
  - Statut R1 et fraction R3.
- [ ] **C5 · Toast**
  - Un seul à la fois, 6 s, maintenu tant qu'il est survolé ou focalisé, `aria-live="polite"`.
  - Actions « Ajouter des détails » (ouvre M3 en modification) et « Annuler » (supprime l'enregistrement).
- [ ] **C6 · `AsyncSection`** : chargement, erreur (« Réessayer »), vide.
  - Supprimer tous les `catch { }` vides.
  - 404/403 → P13.
  - **Interdit** : afficher « 0/0 », « 0 à traiter », « Tout est à jour », un anneau vide, un badge 0 ou un état vide avant la réponse. Valeurs non chargées = `null`, jamais `0` ni `[]`.
  - Squelettes à la forme du contenu, affichés dès 0 ms. Pulsation d'opacité 1 → 0,5 sur 1,2 s, désactivée avec `prefers-reduced-motion`.
  - Rechargement d'une donnée déjà affichée : on garde l'ancienne valeur, pas de nouveau squelette.
  - Badge de la nav masqué tant que le compteur n'est pas reçu.
- [ ] **C7 · Footer légal** (existe sur main) : sur toutes les pages, P01 à P15, connecté ou non.
  - Liens : Politique de confidentialité → P14, Conditions d'utilisation → P15, `mailto:` privacy.
  - En bas du contenu, jamais fixé à l'écran. Sous 640 px : au-dessus de C2. Passer aux tokens.
- [ ] **C8 · Bandeau de ré-acceptation des CGU** (existe sur main, `ConsentBanner`) : si `user.consentRequired`, sous C1 sur P07 à P13.
  - **Non bloquant**, sans bouton de fermeture : seule l'acceptation le retire (EDPB 03/2022).
  - Textes `consent.*` inchangés. CGU (acceptation) et politique (information) restent deux phrases distinctes.
  - Succès → disparaît sans rechargement. Échec → message à gauche du bouton.
  - Token `warning` à la place des `amber-*`. Sous 640 px : bouton pleine largeur.
- [ ] Tokens : classes sémantiques (`bg-primary`, `bg-card`, `text-muted-foreground`) à la place de `blue-*` / `gray-*`. Supprimer dégradés et surfaces translucides.

---

## 3. Pages

### Public & onboarding
- [ ] **P01 Présentation** `/{locale}` *(nouvelle)*
  - Titre « Les entretiens de votre maison, au bon moment. », sous-titre, boutons « Créer un compte » / « Se connecter ».
  - Aperçu rendu avec les **vrais composants de P07** et des données de démo `DemoData` codées en dur, sans appel API :
    - maison « Ma maison », 6/8 à jour ;
    - Ramonage en retard de 8 j, Entretien annuel dans 15 j ;
    - dates calculées par rapport à aujourd'hui.
  - Aperçu non interactif : `inert` + `aria-hidden`.
- [ ] **P02 Connexion** `/login`
  - Après connexion : `returnUrl`, sinon P07.
  - Identifiants incorrects : un seul message, « Email ou mot de passe incorrect. ».
  - Compte restreint (art. 18) : « Votre compte est temporairement suspendu. Pour en savoir plus, écrivez à privacy@houseflow.cloud. » (lien `mailto:`).
  - Footer C7, qui remplace le lien isolé vers la politique.
- [ ] **P03 Inscription** `/register`
  - Stepper en 3 étapes, « Continuer » → P05.
  - Case « J'accepte les Conditions générales d'utilisation. » **non cochée par défaut**, requise. « Continuer » est désactivé tant qu'elle n'est pas cochée. Le lien ouvre P15 dans un nouvel onglet.
  - Sous le bouton, mention d'information sur la politique de confidentialité (P14, nouvel onglet). **Jamais une case** : ce n'est pas un consentement art. 7.
  - Garder `id="acceptTerms"` et envoyer `consentAccepted: true`.
  - Avec `?invitation={token}` : email verrouillé, pas de stepper, bouton « Créer mon compte et rejoindre », puis acceptation automatique → P09.
- [ ] **P04 Invitation** `/invitations/{token}`
  - A · non connecté : Créer un compte / J'ai déjà un compte.
  - B · connecté : « Rejoindre la maison » / « Refuser ».
  - C · invitation expirée.
  - Déjà membre → P09.
  - Description du rôle selon le rôle (textes dans la spec).
  - Mention `invitations.privacyNotice` (information Art. 14 de la personne invitée, version 2026-09-28) sous les boutons, que la personne soit connectée ou non, avec son lien vers P14.
- [ ] **P05 Setup · maison** `/setup/house` *(nouvelle)*
  - Nom requis, pré-rempli « Ma maison » ; adresse facultative.
  - « Continuer » → P06. « Passer » → P07.
  - Si l'utilisateur possède déjà une maison → P07.
  - Seul endroit où la première maison est créée : l'inscription n'en crée plus.
- [ ] **P06 Setup · équipements** `/setup/devices` *(nouvelle)*
  - Catalogue, aucune puce cochée par défaut :
    - Chaudière gaz → Entretien annuel, 12 mois ;
    - Détecteur de fumée → Test, 12 mois ;
    - Poêle à bois → Ramonage, 12 mois ;
    - VMC → Nettoyage des bouches, 6 mois ;
    - Pompe à chaleur → Entretien, 24 mois ;
    - Chauffe-eau → Détartrage, 24 mois.
  - « Dernier entretien » par équipement :
    - Année : année en cours et les 10 précédentes, « Plus ancien », « Je ne sais pas » (par défaut) ;
    - Mois : requis si une année est choisie, désactivé sinon ; mois futurs masqués pour l'année en cours.
  - Aperçu de l'échéancier recalculé en direct.
  - « Créer mes {n} entretiens » → P07 avec toast. « Passer » → P09.
- [ ] **P14 Politique de confidentialité** `/privacy` et **P15 CGU** `/terms` (existent sur main) : harmonisation visuelle uniquement.
  - Contenu `*ContentFr/En` : relecture juridique du 2026-09-26, mis à jour le 2026-09-28 par le référent vie privée (e-mail de la personne invitée, droits du locataire et du collaborateur RW).
  - Pages publiques sans C1 ni C8, colonne de lecture de 72 caractères max, footer C7.
  - « Retour à l'accueil » → `/{locale}`.

### Application
- [ ] **P07 Accueil** `/dashboard`
  - Titre « {n} entretien(s) à traiter », sous-titre « dont {r} en retard », anneau « x/y à jour ».
  - **Tous** les entretiens à traiter (pas de limite de 5), groupés « En retard » puis « Dans les 30 jours ».
  - Section « Mes maisons » en lignes C4, triées par statut puis par nom.
  - États :
    - « Tout est à jour · Prochain : {entretien} · {date} » ;
    - aucune maison → « Commencer » → P05.
- [ ] **P08 Maisons** `/houses`
  - Mêmes lignes C4.
  - « Ajouter une maison » → M1, puis P09. C'est le seul point d'entrée pour créer une maison.
  - Supprimer `/houses/new`.
- [ ] **P09 Maison** `/houses/{id}`
  - Fil d'Ariane « Maisons / [sélecteur de maison] ».
  - En-tête : anneau, avatars des membres, menu ⋯ avec Modifier (M1), Membres (M5), Supprimer (M6) pour le propriétaire ; Membres (M5, mode restreint) seul pour un collaborateur RW.
  - Liste des appareils, triée par statut. « Ajouter un appareil » → M2.
- [ ] **P10 Appareil** `/devices/{id}`
  - Lignes C3 des entretiens, avec la périodicité en toutes lettres.
  - Historique avec les colonnes date · entretien · prestataire · coût, et un total. Clic sur une ligne → M3.
  - Supprimer le bloc « statistiques » en dégradé.
  - Menu ⋯ : M2 / M6.

### Compte, admin, erreurs
- [ ] **P11 Compte** `/settings` — une seule page avec les ancres `#profil`, `#preferences`, `#donnees`, `#api`, `#suppression` (toujours en dernier). `#rappels` sera ajoutée par l'issue email, entre `#profil` et `#preferences`.
  - Langue et thème déplacés ici.
  - **Profil** : prénom, nom et email modifiables (rectification RGPD). Bouton « Enregistrer », actif seulement si un champ a changé. Email déjà pris (409) → message sous le champ.
  - **Mes données** (existe sur main) : boutons « Exporter en JSON » et « Exporter en CSV (ZIP) », plus le lien vers P14.
    - Succès : toast « Export téléchargé. ».
    - Erreur 429 (1 export par heure) : message sous les boutons.
    - Fichier non délivré par le navigateur : erreur, jamais un succès.
  - **Supprimer mon compte** (existe sur main) : zone de danger avec les 4 conséquences, bouton → M7.
- [ ] **P12 Administration** `/admin` — harmonisation visuelle uniquement (tokens, C1, C6).
  - Recherche pendant la frappe, 300 ms d'attente.
  - Non-admin → P13 (403).
- [ ] **P13 Erreurs 404 / 403** *(nouvelle)*
  - « Retour à l'accueil » → P07 si connecté, sinon P01.

## 4. Modales
Règles communes :
- focus sur le premier champ, piégé dans la modale ;
- Entrée valide ;
- bouton principal désactivé tant qu'un champ requis est vide ;
- erreur API affichée en bandeau, la modale reste ouverte.

- [ ] **M1 Maison** (créer / modifier) : nom requis (100 caractères max), adresse (200 max).
- [ ] **M2 Appareil** (créer / modifier)
  - Type requis (catalogue de P06 + « Autre »).
  - Nom pré-rempli avec le libellé du type.
  - Marque, modèle et date d'installation facultatifs.
  - Dernier entretien (mois + année, R2), en création uniquement et masqué si le type est « Autre ».
  - Supprimer `/houses/{id}/devices/new`.
- [ ] **M3 Entretien réalisé** (créer / modifier)
  - Date requise, pas dans le futur.
  - Prestataire avec suggestions, coût décimal ≥ 0, note (500 max).
  - « Supprimer » en modification uniquement.
- [ ] **M4 Type d'entretien** (créer / modifier)
  - Nom requis ; fréquence 3 mois / 6 mois / 1 an (par défaut) / 2 ans / Autre.
  - Dernier entretien (mois + année) en création uniquement.
- [ ] **M5 Membres** : changer le rôle, retirer un membre, renvoyer ou annuler une invitation, inviter (email + rôle) ; collaborateur RW : inviter un locataire, renvoyer ou annuler une invitation de locataire.
- [ ] **M7 Suppression du compte** (existe sur main) : case « Je comprends… » + mot de passe.
  - Bouton désactivé tant que la case n'est pas cochée ou que le mot de passe est vide.
  - Focus initial sur la case. **Entrée ne valide pas.**
  - Succès → session effacée, P02 en remplaçant l'historique.
  - Erreur 400 → « mot de passe incorrect » sous le champ, champ vidé.
- [ ] **M6 Confirmation de suppression** : générique, focus sur « Annuler », textes par cas dans la spec.

## 5. API
- [ ] Aligner le statut « pending » sur la règle des 30 jours (R1).
- [ ] Type d'entretien sans historique : `nextDueDate = createdAt + 30 j`. Valeur « Plus ancien » : `nextDueDate = createdAt`.
- [ ] Création d'appareil ou de type avec « dernier entretien » (mois + année) → enregistrement au 1er du mois.
- [ ] Liste complète des entretiens à traiter pour l'Accueil (retirer la limite `GetUpcomingTasksAsync(5)`).
- [ ] Suppression d'un enregistrement d'entretien, avec recalcul de l'échéance.
- [ ] `AuthService.RegisterAsync` : ne plus créer « Ma maison » ni l'adhésion Owner. La première maison est créée par P05.
  - Adapter les tests backend et E2E qui supposaient cette maison, et `PROJECT_KNOWLEDGE.md`.
  - Un invité n'a que la maison partagée.
- [ ] Compte restreint (art. 18) : code d'erreur distinct au login et au refresh (par ex. `account_restricted`).
- [ ] Invitations : refuser ; renvoyer et annuler une invitation en attente ; conserver le token pendant l'inscription et l'accepter automatiquement.

## RGPD — contraintes (développé sur main, #132–#139)
- Textes légaux figés : P14/P15 et clés i18n `legal.*`, `consent.*`, `account.*`, `footer.*`, `auth.acceptTerms*`, `auth.privacyNotice*`, `invitations.privacyNotice*`. Toute modification suit la procédure de `CLAUDE.md`.
- Conserver les sélecteurs de `e2e/tests/gdpr-*.spec.ts` : `#acceptTerms`, `#acceptUpdatedTerms`, `data-testid` `profile-*`, `save-profile`, `export-*`, `open-delete-account`, `delete-acknowledge`, `delete-password`, `confirm-delete-account`, `delete-error`.
- Aucun traceur tiers, donc aucun bandeau cookies.

## Ordre de mise en œuvre (commits de la PR)
Chaque étape laisse l'app fonctionnelle et la CI au vert.
1. **Fondations** : tokens sémantiques (`globals.css`, dont `warning`), R4 (dates), C6 `AsyncSection`, C5 toast.
2. **Coquille** : C1, C2, C7, C8 intégrés aux layouts ; P13 ; routage R6 et redirections.
3. **API** : tous les points de la section 5, avec leurs tests. Mettre à jour `specs/openapi.yaml` d'abord (API-first).
4. **Cœur** : C3, C4, P07, P08, P09, P10, puis M1 à M6.
5. **Onboarding** : P01 (avec `DemoData`), P03, P04, P05, P06, et la suppression de la maison créée à l'inscription.
6. **Compte** : P11, M7, P14, P15, P12.
7. **Finition** : mobile (R7) sur toutes les pages, E2E, `PROJECT_KNOWLEDGE.md`.

## Hors périmètre
- Rappels par email et préférences de rappel → issue séparée.
- Mot de passe oublié, changement de mot de passe.
- Planification de rendez-vous (retirée volontairement).
- Filtres et recherche sur l'Accueil.

## Définition de « terminé »
- Chaque page et chaque modale correspondent à la spec, y compris leurs états (chargement, vide, erreur) et la version mobile.
- Aucun `catch { }` vide.
- Aucune couleur Tailwind brute (`blue-*`, `gray-*`) dans `Features/`.
- Tests E2E existants mis à jour pour les nouvelles routes et modales ; `gdpr-*.spec.ts` au vert sans modification des sélecteurs.
