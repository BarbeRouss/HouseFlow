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

---

### Proposition 1 : "Flow Wave" (Vague fluide)

**Fichier** : `proposals/proposal-1-flow-house.svg`

**Concept** : Une maison dont l'interieur est anime par des ondes sinusoidales representant le "flow" - le flux continu de maintenance.

**Motivation** :
- La maison est immediatement reconnaissable (secteur immobilier)
- Les vagues representent la continuite, la regularite, le flux - coeur du concept HouseFlow
- Le degrade indigo donne un aspect moderne et technologique
- Le texte **House** (indigo) + **Flow** (navy) met en avant les deux concepts
- Tagline : "MAINTENANCE SIMPLIFIEE"

**Quand l'utiliser** : Header de l'app, favicon (icone maison seule), emails transactionnels.

**Forces** : Tres lisible, concept clair, bon equilibre entre professionnel et moderne.
**Faiblesses** : Design relativement classique dans le secteur proptech.

---

### Proposition 2 : "Shield Home" (Bouclier protecteur)

**Fichier** : `proposals/proposal-2-shield-home.svg`

**Concept** : Un bouclier contenant une maison et un checkmark. La maintenance comme protection active de votre patrimoine.

**Motivation** :
- Le bouclier evoque la **protection**, la **securite**, la **fiabilite**
- Le checkmark valide que tout est sous controle
- Positionne HouseFlow comme un gardien de votre maison
- Degrade vertical indigo → indigo fonce pour un rendu premium
- Le texte **House** (navy) + **Flow** (indigo) inverse le contrast par rapport a P1
- Tagline : "VOTRE MAISON, PROTEGEE"

**Quand l'utiliser** : Communications rassurantes, onboarding, page de connexion.

**Forces** : Inspire confiance, forte connotation de valeur (protection du patrimoine).
**Faiblesses** : Peut evoquer un antivirus ou une assurance plus qu'une app de maintenance.

---

### Proposition 3 : "Circular Flow" (Cycle continu)

**Fichier** : `proposals/proposal-3-circular-flow.svg`

**Concept** : Une maison dans un cercle avec une fleche circulaire evoquant le cycle perpetuel de maintenance. Fond indigo plein.

**Motivation** :
- Le cercle + fleche = **cycle**, **periodicite**, **recurrence** (coeur du metier)
- La maison blanche sur fond indigo offre un contraste maximal
- Le style "app icon" fonctionne parfaitement comme icone mobile/favicon
- Le texte en minuscules **houseflow** donne un aspect moderne et tech
- Weight extra-bold (800) pour une presence forte
- Tagline : "ENTRETIEN EN CONTINU"

**Quand l'utiliser** : Icone d'app mobile, favicon, avatar sur les stores, reseaux sociaux.

**Forces** : Excellent en petit format, tres "app-native", moderne.
**Faiblesses** : Le cercle avec fleche est un motif generique (recyclage, refresh).

---

### Proposition 4 : "Monogramme HF" (Lettres fusionnees)

**Fichier** : `proposals/proposal-4-monogram-hf.svg`

**Concept** : Les lettres H et F fusionnees dans un carre arrondi. Le H est stylise en forme de maison (toit triangulaire), la barre du H est une onde "flow".

**Motivation** :
- Monogramme memorable et unique - devient une signature visuelle
- Le H se transforme en maison grace au toit triangulaire
- La barre transversale ondulee du H rappelle le "flow"
- Le F adjacent complete le mot "HF" pour HouseFlow
- Le carre arrondi (rx:22) est le standard actuel des icones d'apps
- Le degrade tri-tons indigo donne de la profondeur
- Dans le texte, les H et F sont en indigo pour rappeler le monogramme
- Tagline : "SMART HOME MAINTENANCE"

**Quand l'utiliser** : Favicon, app icon, watermark, badge de confiance.

**Forces** : Le plus distinctif, forte memorisation, versatile en taille.
**Faiblesses** : Moins explicite que les propositions figuratives, necessite familiarisation.

---

## Recommandation

| Critere               | P1 Flow Wave | P2 Shield | P3 Circular | P4 Monogramme |
| --------------------- | :----------: | :-------: | :---------: | :-----------: |
| Lisibilite            | ++++         | +++       | ++++        | ++            |
| Originalite           | ++           | ++        | +++         | ++++          |
| Versatilite (tailles) | +++          | ++        | ++++        | ++++          |
| Evocation du metier   | ++++         | +++       | +++         | ++            |
| Aspect premium        | +++          | ++++      | +++         | ++++          |
| Favicon / app icon    | ++           | ++        | ++++        | ++++          |

**Ma recommandation** : **Combinaison P3 (icone) + P1 (logo complet)**

- Utiliser **P3 Circular Flow** comme **icone / favicon** (excellent en petit format, identifiable)
- Utiliser **P1 Flow Wave** comme **logo horizontal** (header, emails, documents)
- Cela offre un systeme de marque coherent avec deux niveaux de lecture

---

## Declinaisons prevues

### Formats
- **Horizontal** : Logo + texte (header, emails)
- **Icone seule** : Pour favicon (16x16, 32x32), app icon (192x192, 512x512)
- **Vertical** : Logo au-dessus du texte (splash screen, print)

### Variantes
- **Sur fond clair** : Logo indigo + texte navy
- **Sur fond sombre** : Logo blanc/indigo clair + texte blanc
- **Monochrome blanc** : Pour fonds colores
- **Monochrome noir** : Pour impressions N&B

---

## Prochaines etapes

1. **Choisir** la proposition preferee (ou la combinaison recommandee)
2. **Affiner** le logo choisi (proportions exactes, espacement, kerning)
3. **Generer** les declinaisons (dark mode, monochrome, tailles)
4. **Integrer** dans l'app (favicon, header, meta OG images)
5. **Installer** la police Inter dans le projet Next.js
