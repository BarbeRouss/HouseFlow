# Handoff : Refonte UI HouseFlow (hi-fi)

## Vue d'ensemble
Cette refonte **visuelle** complète la refonte UX déjà implémentée (spec v2). Elle vise des écrans plus chaleureux et plus lisibles, en clair et en sombre, du mobile au desktop. Le parcours, les règles métier et les routes **ne changent pas**, sauf les points listés dans la section « Écarts avec la spec v2 ».

## Les fichiers de design
Les fichiers HTML de ce dossier sont des **références de design** : ils montrent le rendu et le comportement attendus, ce n'est pas du code à copier. Le travail consiste à **recréer ces écrans dans le code existant** : Blazor (`src/HouseFlow.Web`), Tailwind avec des tokens sémantiques dans `Styles/app.input.css`, et les composants partagés C1 à C8 de la spec v2.

Les fichiers s'ouvrent directement dans un navigateur, hors ligne :

| Fichier | Contenu |
|---|---|
| `ecrans-houseflow.html` | **Référence principale.** Les 15 pages, chacune en mobile 390 / tablette 768 / desktop 1280, clair et sombre. Sommaire en tête. |
| `popups-houseflow.html` | Section 4 : les modales M1 à M7, le toast C5 et le panneau mobile. Les autres sections de ce fichier sont des étapes intermédiaires : **en cas de doute, `ecrans-houseflow.html` fait foi.** |
| `spec-refonte-v2.html` | Spec fonctionnelle (règles R1 à R7, composants C1 à C8, états, rôles). Elle reste la source de vérité pour le **comportement**. |
| `assets/` | Icône de l'app (8 SVG). |

**Ordre de priorité en cas de conflit :** ce README, puis `ecrans-houseflow.html`, puis `popups-houseflow.html`, puis `spec-refonte-v2.html`.

Les arbitrages pris en implémentant cette maquette (textes légaux figés, footer partout, couleur du badge de nav, badge « Partagée »…) sont listés dans [`docs/design/01-refonte-ux.md`](../../docs/design/01-refonte-ux.md) § 6 ; ils l'emportent sur les maquettes.

## Fidélité
**Hi-fi.** Couleurs, typographie, espacements, rayons et textes sont définitifs. Seules les données sont fictives (Marc Renard, Maison de Namur…).

---

## Écarts avec la spec v2 (à implémenter)
1. **Pas de photo de maison.** Chaque maison reçoit une **couleur** à la création, prise en rotation dans la palette « Couleurs de maison » ci-dessous. Elle est stockée côté API (`House.colorKey`) et s'affiche en bandeau de carte, en bandeau de page (P09) et dans l'invitation (P04).
2. **P06 : chaque équipement coché affiche 3 champs**, dans cet ordre :
   - **Nom de l'entretien** : pré-rempli depuis le catalogue, modifiable, requis, 100 caractères maximum. S'il est vidé, il reprend la valeur par défaut quand on quitte le champ.
   - **Fréquence** : liste 3 mois / 6 mois / 1 an / 2 ans, pré-remplie depuis le catalogue.
   - **Dernier entretien** : mois et année, règle R2.
3. **P11 Compte : une seule colonne de 720 px, sans menu latéral.** Les ancres `#profil`, `#preferences`, etc. restent dans l'URL.
4. **Icône de l'app : maison-calendrier dont une case prend la couleur du statut** (détails dans la section Assets).
5. **Mode sombre** complet, piloté par la préférence Thème de P11 (Clair / Sombre / Système, « Système » par défaut).
6. **Sous 640 px, P07 masque le menu ⋯** des lignes d'entretien. « Fait à une autre date » reste accessible par l'action « Ajouter des détails » du toast. Sur P10, le menu ⋯ reste visible.
7. **« C'est fait » garde toujours son libellé** : icône `check` + texte, y compris sur mobile.

---

## Design tokens

### Typographie
- **Titres :** Bricolage Grotesque, graisses 600 et 700, **auto-hébergée** (`wwwroot/fonts`, jamais chargée depuis Google Fonts : ce serait transmettre l'IP du visiteur à un tiers). Réglages : letter-spacing −0,02 à −0,028 em, `text-wrap: balance`.
- **Texte et interface :** Instrument Sans, graisses 400, 500, 600 et 700, auto-hébergée elle aussi.
- **Icônes :** Lucide, déjà utilisé dans le code.

| Rôle | Mobile | Tablette | Desktop | Style |
|---|---|---|---|---|
| Titre du hero P01 | 38 | 52 | 56 | Bricolage 700, line-height 1.02 |
| H1 d'une page de l'app | 30 | 38 | 44 | Bricolage 700, line-height 1.05 |
| H1 d'un formulaire (auth, setup) | 28 | 32 | 34 | Bricolage 700, line-height 1.1 |
| H2 d'une section | 22 | 22 | 22 | Bricolage 600 |
| Corps de texte | 16 | 16 | 16 | Instrument 400, line-height 1.5 |
| Titre d'une ligne | 16 | 16 | 16 | Instrument 600 |
| Méta, sous-titre | 14 | 14 | 14 | Instrument 400, couleur `muted` |
| Libellé de section (EN RETARD…) | 13 | 13 | 13 | 700, majuscules, letter-spacing .06em |
| Pastille, badge | 12–13 | | | 600–700 |

**Règle mobile :** les champs de saisie sont en **16 px minimum**, pour éviter le zoom automatique d'iOS.

### Couleurs : clair / sombre
| Token | Clair | Sombre | Usage |
|---|---|---|---|
| `background` | `#f7f5f1` | `#121117` | Fond de page (beige chaud, pas de gris froid) |
| `card` | `#ffffff` | `#1c1b22` | Cartes, listes, header |
| `foreground` | `#18171f` | `#f2f0f6` | Texte |
| `muted-foreground` | `#57545f` | `#a9a5b3` | Texte secondaire |
| `border` | `#e9e5de` | `#2c2a34` | Bordure des cartes, piste de l'anneau |
| `input` | `#d9d3c9` | `#3d3a47` | Bordure des champs et des boutons secondaires |
| `divider` | `#f0ece6` | `#26242d` | Séparateur entre les lignes |
| `surface-soft` | `#fbfaf7` | `#18171d` | En-tête de tableau |
| `input-bg` | `#ffffff` | `#16151b` | Fond des champs |
| `segment` | `#f0ede7` | `#26242d` | Fond des contrôles segmentés, barre de navigateur |
| `primary` | `#6366f1` | `#6366f1` | Fond des boutons principaux, anneau, cases cochées |
| `primary-text` | `#4338ca` | `#a5b4fc` | Liens, onglet actif, bouton « C'est fait » en contour |
| `primary-soft` | `#eef0ff` | `#27274f` | Fond de l'onglet actif, badge ADMIN |
| `brand-panel` | `#6366f1` | `#4f46e5` | Panneau de P02 |
| `late` / `late-soft` | `#b91c1c` / `#fef2f2` | `#f87171` / `#3b1a1e` | En retard (R1) |
| `due` / `due-soft` | `#a35200` / `#fff7ed` | `#fbbf24` / `#352710` | À faire (R1) |
| `ok` / `ok-soft` | `#15803d` / `#f0fdf4` | `#4ade80` / `#12301d` | À jour (R1) |
| `destructive` | `#dc2626` | `#dc2626` | Boutons de suppression, badge de la nav |
| `destructive-border` | `#fecaca` | `#5b2328` | Bordure de la section « Supprimer mon compte » |

Le texte sur `primary` est toujours `#ffffff`.

### Couleurs par type d'appareil (tuile d'icône)
Une teinte par type. La tuile et l'icône en sont dérivées :

| Type | Teinte | Icône Lucide |
|---|---|---|
| Chaudière / chauffage | `#ea580c` | `flame` |
| Poêle à bois | `#dc2626` | `flame-kindling` |
| VMC | `#0284c7` | `wind` |
| Chauffe-eau | `#2563eb` | `droplets` |
| Détecteur de fumée | `#ca8a04` | `alarm-smoke` |
| Pompe à chaleur | `#16a34a` | `fan` |
| Autre | `muted` | `wrench` |

- **Clair :** fond = 14 % teinte + 86 % blanc ; icône = 84 % teinte + 16 % noir.
- **Sombre :** fond = 26 % teinte + 74 % `card` ; icône = 60 % teinte + 40 % blanc.
- En CSS : `color-mix(in srgb, <teinte> 14%, white)`, etc.

### Couleurs de maison (bandeau)
Palette en rotation : `#6366f1`, `#ea580c`, `#16a34a`, `#0284c7`, `#ca8a04`, `#db2777`.
- **Clair :** fond = 20 % teinte + blanc ; texte et icônes = 62 % teinte + noir.
- **Sombre :** fond = 34 % teinte + `card` ; texte et icônes = 50 % teinte + blanc.
- **Pastilles posées sur le bandeau :** `rgba(255,255,255,.78)` en clair, `rgba(0,0,0,.28)` en sombre.
- **Décor :** icône `house` à 22 % d'opacité, en haut à droite.

### Rayons
- Pastilles et badges : 99 px
- Champs : 10 px (9 px pour les listes déroulantes compactes)
- Boutons : 10 px (44 px de haut) ; boutons pleine largeur : 12 px (≈ 50 px de haut)
- Tuile d'icône : 12–13 px (44–46 px) ; 20 px pour la tuile de 72 px (P10)
- Cartes et listes : 16 px ; bandeau de maison : 18 px
- Modales : 20 px ; panneau mobile : 22 px en haut

### Ombres
- Cartes : **aucune**, seulement une bordure `border` de 1 px.
- Aperçu de P01 : `0 34px 70px -30px rgba(40,30,20,.45)` en clair, `rgba(0,0,0,.8)` en sombre, plus un anneau de 1 px en `border`.
- Bouton principal du hero : `0 8px 18px -8px rgba(99,102,241,.7)`.
- Modales : `0 30px 60px -20px rgba(0,0,0,.4)`. Fond assombri : `rgba(24,23,31,.45)`.
- Toast : `0 18px 40px -12px rgba(0,0,0,.45)`, fond `#18171f` dans les deux thèmes.

### Espacement
- **Marge horizontale de page :** 20 px (mobile), 32 px (tablette), 40 px (desktop).
- **Largeur de contenu :**
  - pages de l'app : max 1000 px ;
  - P11 et pages légales : 720 px ;
  - auth et setup : 520 px (connexion : 400 px) ;
  - P06 : 1160 px.
- Espacement entre blocs : 26 px. Dans une liste : lignes avec un padding de 14 px × 16 px, séparateur `divider` de 1 px.
- **Zones tactiles : 44 px minimum partout.**

---

## Points de rupture (R7)
| Largeur | Changements |
|---|---|
| < 640 px | Header réduit (logo seul, 56 px) et **barre d'onglets en bas** (Accueil · Maisons · Compte, 64 px). Une colonne. Boutons des formulaires empilés, action principale en haut. Échéance sous le titre dans les lignes. Fil d'Ariane remplacé par un lien « ‹ Parent ». Colonnes secondaires masquées (pastille de statut, prestataire, date de l'historique), leur contenu passant dans la ligne de méta. |
| 640–1023 px | Header complet (64 px). Cartes de maisons sur 2 colonnes. Hero P01 centré, aperçu incliné vers l'arrière (`perspective(1800px) rotateX(9deg) scale(.97)`). P02 sans panneau indigo. P06 sur une colonne, échéancier sous la liste. |
| ≥ 1024 px | Cartes sur 3 colonnes. Hero P01 sur 2 colonnes (440 px + reste), aperçu `perspective(2200px) rotateY(-9deg) rotateX(3deg)`. P02 avec panneau indigo de 520 px. P06 avec l'échéancier à droite (340 px). |

---

## Composants (mise à jour visuelle de C1 à C8)

**C1 Header (≥ 640 px).** Hauteur 64 px, fond `card`, bordure basse `border`.
- Logo (icône de statut 28 px + « HouseFlow » en Bricolage 700 19 px).
- Entrées : icône Lucide 17 px + libellé 15/600, padding 8 px × 14 px, rayon 9 px.
- Entrée active : fond `primary-soft`, texte `primary-text`. Inactive : texte `muted`.
- Badge sur Accueil : `destructive`, texte blanc 12/700.
- Avatar de 36 px à droite, initiales 13/700.

**C2 Barre d'onglets (< 640 px).** Hauteur 64 px. 3 entrées égales : icône 21 px + libellé 12/600. Entrée active en `primary-text`. Badge 11/700 décalé en haut à droite de l'icône.

**C3 Ligne d'entretien.**
- Tuile 44 px (couleur du type) ; titre 16/600 ; méta « appareil · maison » 14 `muted`, sur une ligne avec ellipse.
- Échéance 15/600 dans la couleur du statut : colonne à droite au-dessus de 640 px, ligne sous le titre en dessous (13/600).
- **Bouton « C'est fait »** : 44 px de haut, icône `check` + libellé 14/600.
  - En retard : **plein**, fond `primary`, texte blanc.
  - Sinon : **contour**, fond `card`, bordure `input`, texte `primary-text`.
- Menu `ellipsis` de 32 × 44 px.
- Sur P10, la tuile est remplacée par un point de 10 px dans la couleur du statut, et la méta affiche la fréquence.

**C4 Carte de maison.**
- Bandeau de 104 px à la couleur de la maison, avec les icônes de ses appareils (pastilles de 32 px) et le badge « Partagée » (`users`) si la maison est partagée.
- Dessous : nom 17/600, « ville · n appareils », pastille de statut (point de 7 px + libellé 13/600 sur fond `*-soft`) et fraction « x/y à jour ».

**C4 Ligne d'appareil (P09).** Tuile 46 px, nom, méta, pastille de statut, fraction, chevron.

**Anneau de score.**
- SVG, rayon 44 dans une boîte de 104, trait de 12 : piste `border`, progression `primary`, extrémités arrondies, départ à −90°.
- Fraction au centre en Bricolage 700.
- Tailles : 64 px (P07), 56 px (P09), 104 px (aperçu).

**C5 Toast.**
- Fond `#18171f` (identique en sombre), rayon 14, pastille de succès de 26 px (`#22c55e`, icône `check`).
- Titre 15/600 et sous-titre « Prochain : … » 13 en `#c9c6d0`.
- Actions « Annuler » et « Ajouter des détails » à droite.
- Position : en bas au centre sur desktop ; sur mobile, 12 px au-dessus de la barre d'onglets.

**C6 États asynchrones.** Aucune valeur par défaut affichée avant la réponse (spec C6).
- Squelettes : fond `border`, pulsation d'opacité 1 → 0,5 sur 1,2 s.
- Pendant le chargement, l'icône de l'app est en variante `none`.

**C7 Footer.** Liens 13 px `muted` centrés : Confidentialité · Conditions d'utilisation · privacy@houseflow.cloud · © 2026 HouseFlow. Bordure haute `border`.

**C8 Bandeau CGU.** Fond `#fffbeb`, bordure basse `#fde68a`, texte `#78350f` (voir `popups-houseflow.html`, écran 3f). Il ne se ferme pas : seul « J'accepte » le retire.

**Champs.**
- 50 px de haut (44 px en compact), bordure 1 px `input`, fond `input-bg`, rayon 10, texte 16.
- Focus : bordure 2 px `primary`.
- Libellé 15/600 au-dessus ; mention « · facultatif » en 400 `muted`.

**Contrôle segmenté.** Fond `segment`, padding 3 px, rayon 10. Option active : fond `card`, texte `foreground`, ombre `0 1px 3px rgba(0,0,0,.14)`.

**Étapes (P03, P05, P06).**
- 3 barres de 4 px (`primary` jusqu'à l'étape en cours, `border` ensuite), espacées de 8 px.
- Libellés « n · Compte / Maison / Équipements » au-dessus de 640 px ; « Étape n sur 3 » à droite du logo en dessous.

---

## Pages (voir `ecrans-houseflow.html` pour chaque page, format et thème)
| ID | Route | Points visuels clés |
|---|---|---|
| P01 | `/{locale}` | Hero + aperçu du dashboard dans une **fenêtre de navigateur** (3 points, barre d'adresse `houseflow.cloud/fr/dashboard`), fondu vers `background` sur les 110 derniers px. L'aperçu est rendu avec **les vrais composants de P07 et des données de démo** (pas de capture d'écran). Au-dessus de 640 px : dashboard desktop à l'échelle 0,7. En dessous : version mobile, à plat. |
| P02 | `/login` | Formulaire de 400 px. Erreur en bandeau `late-soft`. Panneau indigo ≥ 1024 px avec 3 lignes d'entretien et une phrase d'accroche. |
| P03 | `/register` | Étapes. Prénom et nom sur 2 colonnes. Case CGU (24 px, cochée = `primary`). |
| P04 | `/invitations/{token}` | Bandeau de la maison (150 px), « {invitant} vous invite à rejoindre », carte de rôle, boutons Rejoindre (2/3) et Refuser (1/3). |
| P05 | `/setup/house` | Tuile de 64 px à la couleur de la future maison, 2 champs, « Continuer » et « Passer ». |
| P06 | `/setup/devices` | Catalogue en cartes (bordure 2 px `primary` si coché), 3 champs par équipement coché (voir les écarts), échéancier en direct. Mobile : barre fixe en bas « n entretiens · Voir l'échéancier » + bouton « Créer ». |
| P07 | `/dashboard` | « Bonjour {prénom} », H1 « n entretiens à traiter », « dont r en retard » avec un point `late`, carte de l'anneau. Groupes EN RETARD et DANS LES 30 JOURS. Section « Mes maisons » + « Tout voir ». |
| P08 | `/houses` | H1 + « Ajouter une maison » (icône `plus`), grille de cartes. |
| P09 | `/houses/{id}` | Fil d'Ariane, bandeau de 130 px, H1 + adresse (`map-pin`), menu ⋯ (44 px), anneau + avatars des membres, liste des appareils + « Ajouter ». |
| P10 | `/devices/{id}` | Tuile de 72 px + H1 + méta, entretiens, historique en tableau (4 colonnes ≥ 640 px) + « Total : x € ». |
| P11 | `/settings` | Sections empilées : Profil, Préférences (Langue, Thème), Mes données (JSON, CSV), Clés API, Applications connectées (`#applications` : une ligne par application autorisée via OAuth — nom, droits, date — et « Révoquer » → M6), Supprimer mon compte (bordure `destructive-border`, titre `late`). |
| P12 | `/admin` | 4 statistiques (2 × 2 sur mobile), recherche avec icône, liste d'utilisateurs + badge ADMIN. |
| P13 | toute URL | Illustration : tuile maison inclinée de −6° + pastille « ? » (cadenas pour la variante 403). « Retour à l'accueil ». |
| P14 / P15 | `/privacy`, `/terms` | Colonne de lecture de 720 px, H2 par section, texte 16/1.65, encart contact. **Garder les textes actuels** : ceux des maquettes sont provisoires. |
| P16 | `/oauth/authorize`, `/oauth/consent` | Connexion d'une application tierce (OAuth, ex. Claude). Session requise, sans C1/C2/C8 : logo + colonne de 520 px (comme P04/P05). `/oauth/authorize` n'affiche que « Connexion à {hôte}… ». Consentement : tuile 72 px, H1 = nom de l'application, « souhaite accéder à votre compte HouseFlow », compte connecté, droits demandés en cases cochées, hôte de retour, avertissement anti-hameçonnage (`warning-soft`), Autoriser (primary) / Refuser (outline). Lien invalide ou application inconnue : carte d'erreur, sans redirection. |

## Popups (voir `popups-houseflow.html`, section 4)
- **Structure commune :** largeur 520 px ; en-tête avec titre (Bricolage 600, 22–24) et bouton fermer (`x`) ; contenu en padding 24 ; pied de modale en fond `surface-soft` avec « Annuler » à gauche de l'action principale.
- **Sous 640 px :** panneau ancré en bas, poignée de 40 × 5 px, boutons pleine largeur.
- **Suppressions (M6, M7) :** bouton `destructive`, focus initial sur « Annuler ».
- **M7 :** bouton désactivé tant que la case n'est pas cochée et le mot de passe saisi.
- **M2 :** grille de 4 types (tuiles), avec « Dernier entretien » dans un encart `background`.
- **M3 :** suggestions de prestataires en liste déroulante sous le champ.
- **M5 :** invitation en attente affichée avec un avatar à bordure pointillée et le lien « Renvoyer ».

---

## Assets : icône de l'app
Maison-calendrier sur une tuile `#6366f1` (rayon 28/120). Une case du calendrier prend la couleur du statut global, le plus urgent de toutes les maisons visibles (même donnée que le badge R3).

| Fichier | Quand |
|---|---|
| `assets/icon-ok.svg` / `favicon-ok.svg` | Tout est à jour. **C'est aussi l'icône fixe** : pages publiques, PWA, e-mails. |
| `icon-due.svg` / `favicon-due.svg` | Au moins 1 entretien à faire, aucun en retard. |
| `icon-late.svg` / `favicon-late.svg` | Au moins 1 entretien en retard. |
| `icon-none.svg` / `favicon-none.svg` | Statut en chargement, aucun entretien, ou onboarding. Jamais de vert avant d'avoir la donnée. |

- `icon-*` : logo du header (28 px) et tailles ≥ 32 px.
- `favicon-*` : version simplifiée (une seule case) pour le `<link rel="icon">` et les tailles < 32 px. Mettre à jour le lien à chaque changement de statut.
- À générer : PNG PWA 192 et 512 à partir de `icon-ok.svg`. Remplacer l'actuel `wwwroot/favicon.svg`.
- La couleur n'est jamais la seule information : le badge chiffré et les libellés restent affichés.

## État et données
Pas de nouvel état par rapport à la spec v2, sauf :
- `House.colorKey` (enum de 6 valeurs, attribué à la création en rotation, modifiable plus tard si besoin) ;
- `User.theme` (`light` | `dark` | `system`), appliqué via une classe `.dark` sur `<html>`, avant le premier rendu pour éviter un flash de thème ;
- le statut global exposé à C1, C2 et au favicon (même source que le badge).

## Vérification (définition de « terminé »)
- [ ] Chaque page correspond à `ecrans-houseflow.html` dans les 6 combinaisons (3 largeurs × 2 thèmes).
- [ ] Aucune couleur écrite en dur dans `Features/` : uniquement les tokens ci-dessus.
- [ ] Contraste de texte ≥ 4,5:1 dans les deux thèmes (les tokens proposés le respectent).
- [ ] Zones tactiles ≥ 44 px ; champs en 16 px sur mobile.
- [ ] Le favicon change de couleur selon le statut ; icône neutre pendant le chargement.
