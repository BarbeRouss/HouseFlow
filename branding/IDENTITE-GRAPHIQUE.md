# HouseFlow - Identite Graphique

## Contexte

HouseFlow est une application de suivi de maintenance immobiliere. Elle aide les proprietaires et gestionnaires a monitorer l'entretien de leurs biens : equipements, periodicites, historique, scores de sante. Le domaine cible est **flow.house**.

**Valeurs de marque** : Fiabilite, Simplicite, Continuite, Protection du patrimoine.

**Cibles** : Proprietaires, gestionnaires de biens, collaborateurs techniques.

---

## Palette de couleurs (commune aux 4 propositions)

### Couleurs primaires

| Role           | Hex       | HSL                | Usage                          |
| -------------- | --------- | ------------------ | ------------------------------ |
| **Indigo 500** | `#6366f1` | `239, 84%, 67%`   | Couleur principale, CTA, liens |
| **Indigo 400** | `#818cf8` | `235, 90%, 75%`   | Hover, degrades                |
| **Indigo 700** | `#4338ca` | `243, 55%, 51%`   | Texte accent, contraste        |
| **Indigo 200** | `#c7d2fe` | `228, 96%, 89%`   | Backgrounds legers             |

### Couleurs secondaires

| Role              | Hex       | Usage                         |
| ----------------- | --------- | ----------------------------- |
| **Navy 950**      | `#1e1b4b` | Titres, texte fort            |
| **Slate 500**     | `#64748b` | Texte secondaire, taglines    |
| **Slate 100**     | `#f1f5f9` | Fonds clairs                  |
| **White**         | `#ffffff` | Cartes, conteneurs            |

### Couleurs semantiques (existantes)

| Statut      | Hex       | Usage                    |
| ----------- | --------- | ------------------------ |
| **Vert**    | `#22c55e` | A jour / complete        |
| **Orange**  | `#f59e0b` | En attente / a venir     |
| **Rouge**   | `#ef4444` | En retard / critique     |

---

## Typographie

### Recommandation : **Inter**

- **Titres** : Inter Bold (700) / Extra Bold (800)
- **Corps** : Inter Regular (400) / Medium (500)
- **Labels/Tags** : Inter Medium (500), uppercase, letter-spacing: 2-3px
- **Fallback** : system-ui, -apple-system, sans-serif

Inter est une police sans-serif moderne, excellente lisibilite sur ecran, gratuite (Google Fonts), avec un large eventail de graisses. Elle s'integre parfaitement avec l'esthetique Shadcn/ui deja en place.

### Alternative : **Plus Jakarta Sans**
Plus ronde et chaleureuse, pour un positionnement plus "friendly".

---

## Propositions de logo

**Principe** : Tous les logos sont des icones pures, sans texte ni tagline, pour fonctionner a l'international sans barriere linguistique. Chaque proposition existe en variante light et dark.

---

### Proposition 1 : "Flow Wave" (Vague fluide)

**Fichiers** : `proposals/proposal-1-flow-house.svg` / `proposal-1-flow-house-dark.svg`

**Concept** : Silhouette de maison remplie d'un degrade indigo, avec deux ondes sinusoidales blanches a l'interieur representant le "flow".

**Motivation** :
- La maison est immediatement reconnaissable (secteur immobilier)
- Les vagues evoquent la continuite, la regularite, le flux - coeur du concept HouseFlow
- Double onde : la premiere nette (maintenance active), la seconde en transparence (maintenance planifiee)
- Le degrade indigo donne un aspect moderne et technologique
- Forme simple qui fonctionne a toutes les tailles

**Forces** : Concept clair et universel, bon equilibre entre professionnel et moderne.
**Faiblesses** : Design relativement classique dans le secteur proptech.

---

### Proposition 2 : "Shield Home" (Bouclier protecteur)

**Fichiers** : `proposals/proposal-2-shield-home.svg` / `proposal-2-shield-home-dark.svg`

**Concept** : Un bouclier indigo contenant une maison blanche et un checkmark. La maintenance comme protection active du patrimoine.

**Motivation** :
- Le bouclier evoque la **protection**, la **securite**, la **fiabilite**
- Le checkmark valide que tout est sous controle - comprehensible dans toutes les cultures
- Degrade vertical indigo → indigo fonce pour un rendu premium
- La combinaison bouclier + check est universelle (pas besoin de texte)

**Forces** : Inspire confiance, forte connotation de valeur, symbolisme universel.
**Faiblesses** : Peut evoquer un antivirus ou une assurance plus qu'une app de maintenance.

---

### Proposition 3 : "Circular Flow" (Cycle continu)

**Fichiers** : `proposals/proposal-3-circular-flow.svg` / `proposal-3-circular-flow-dark.svg`

**Concept** : Maison blanche inscrite dans un cercle indigo, avec une fleche circulaire en filigrane evoquant le cycle perpetuel de maintenance. Porte coloree en indigo.

**Motivation** :
- Le cercle + fleche = **cycle**, **periodicite**, **recurrence** (coeur du metier)
- La maison blanche sur fond indigo offre un contraste maximal
- La fleche circulaire en transparence ajoute du sens sans surcharger
- Format parfait pour app icon et favicon (carre dans cercle)
- La porte indigo cree un point focal

**Forces** : Excellent en petit format, tres "app-native", moderne, universel.
**Faiblesses** : Le cercle avec fleche est un motif generique (recyclage, refresh).

---

### Proposition 4 : "Monogramme H" (Maison abstraite)

**Fichiers** : `proposals/proposal-4-monogram-hf.svg` / `proposal-4-monogram-hf-dark.svg`

**Concept** : Un H stylise en forme de maison (deux piliers + toit triangulaire) dans un carre arrondi. La barre transversale du H est une onde "flow".

**Motivation** :
- Abstraction memorable et unique - pas de texte, juste un symbole
- Le H evoque "House" par sa forme de maison (toit + piliers)
- La barre ondulee du H rappelle le "flow" de maniere organique
- Le carre arrondi (rx:26) est le standard des icones d'apps modernes
- Le degrade tri-tons indigo donne de la profondeur et du mouvement
- Fonctionne comme un pictogramme universel

**Forces** : Le plus distinctif, forte memorisation, tres versatile en taille, aucune dependance linguistique.
**Faiblesses** : Plus abstrait, necessite un temps de familiarisation.

---

## Recommandation

| Critere                    | P1 Flow Wave | P2 Shield | P3 Circular | P4 Monogramme |
| -------------------------- | :----------: | :-------: | :---------: | :-----------: |
| Lisibilite                 | ++++         | +++       | ++++        | +++           |
| Originalite                | ++           | ++        | +++         | ++++          |
| Versatilite (tailles)      | +++          | ++        | ++++        | ++++          |
| Evocation du metier        | ++++         | +++       | ++++        | +++           |
| Aspect premium             | +++          | ++++      | +++         | ++++          |
| Favicon / app icon         | ++           | ++        | ++++        | ++++          |
| Universalite (sans texte)  | ++++         | ++++      | ++++        | ++++          |

**Ma recommandation** : **P4 Monogramme H**

Le monogramme combine le meilleur des deux mondes :
- **Distinctif** : aucun concurrent n'a ce symbole
- **Universel** : fonctionne sans texte dans toutes les cultures
- **Polyvalent** : aussi lisible en 16x16 (favicon) qu'en grand format
- **Semantique** : maison (toit) + flow (onde) dans un seul symbole

Alternative : **P3 Circular Flow** si on prefere un rendu plus explicite et moins abstrait.

---

## Declinaisons prevues

### Formats
- **Icone seule** : Favicon (16x16, 32x32), app icon (192x192, 512x512)
- **Logo + wordmark** : Icone + "HouseFlow" a cote (header, emails, documents)
- **Logo vertical** : Icone au-dessus du wordmark (splash screen, print)

### Variantes
- **Sur fond clair** : Logo indigo (variante par defaut)
- **Sur fond sombre** : Logo indigo clair (variante dark)
- **Monochrome blanc** : Pour fonds colores
- **Monochrome noir** : Pour impressions N&B

---

## Prochaines etapes

1. **Choisir** la proposition preferee
2. **Affiner** le logo choisi (proportions exactes, epaisseur des traits)
3. **Generer** toutes les declinaisons (tailles, monochrome, avec/sans wordmark)
4. **Integrer** dans l'app (favicon, header, meta OG images, manifest)
5. **Installer** la police Inter dans le projet Next.js
