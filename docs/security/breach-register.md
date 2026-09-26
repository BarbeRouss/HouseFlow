# Registre des violations de données

**Article 33(5) du RGPD**

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | **Rouss Consulting SRL** (service HouseFlow) |
| **Référent vie privée** | `privacy@houseflow.cloud` |
| **Date de création** | 2026-09-11 |
| **Dernière mise à jour** | 2026-09-11 |
| **Durée de conservation** | **5 ans** à compter de la clôture de chaque violation |
| **Procédure applicable** | [breach-notification-procedure.md](./breach-notification-procedure.md) |

---

## 1. Obligation

L'article 33(5) dispose que le responsable du traitement « **documente toute violation** de données à caractère personnel, en indiquant les faits concernant la violation de données à caractère personnel, ses effets et les mesures prises pour y remédier », cette documentation devant permettre à l'autorité de contrôle « de vérifier le respect du présent article ».

**Trois points méritent d'être soulignés, car ils sont fréquemment méconnus :**

1. **L'obligation est sans seuil.** Une violation mineure, circonscrite, sans conséquence apparente, doit être documentée exactement comme une violation majeure.
2. **Elle couvre les violations non notifiées.** Une violation qualifiée « peu susceptible d'engendrer un risque », et donc non notifiée à l'autorité au titre de l'article 33(1), **doit figurer ici avec la motivation écrite du non-signalement**. C'est précisément cette motivation que l'autorité examinera pour apprécier le bien-fondé de la décision de ne pas notifier.
3. **Le registre est autonome.** Ne pas documenter une violation constitue un manquement **distinct** de la violation elle-même, et sanctionnable en tant que tel — même si la gestion de l'incident a par ailleurs été irréprochable.

**Un registre vide n'est pas un registre absent.** L'existence du présent document, daté et maintenu, démontre qu'un dispositif de documentation est en place. C'est la première pièce demandée lors d'un contrôle consécutif à un incident.

---

## 2. Règles de tenue

| Règle | Application |
|---|---|
| **Ouverture de la fiche** | **Dès la prise de connaissance**, avant même la qualification du risque. La fiche est complétée au fil de l'investigation. |
| **Référence** | Format `VIOL-AAAA-NN` — année, puis numéro d'ordre dans l'année (`VIOL-2026-01`). |
| **Horodatage** | Toutes les dates et heures en **UTC**, au format `AAAA-MM-JJ HH:MM`. |
| **Motivation systématique** | La décision de notifier **comme** celle de ne pas notifier doivent être motivées par écrit. Une case « non » sans motif est un défaut. |
| **Minimisation** | Le registre est lui-même un traitement de données. **Ne jamais y consigner de données personnelles issues de la violation.** Décrire les catégories et les volumes, jamais les valeurs. Ne pas y coller d'extraits de base ni de journaux : les archiver séparément, en accès restreint, et se contenter d'y renvoyer. |
| **Preuves** | Journaux, captures et messages sont archivés **hors du dépôt Git**, dans l'espace documentaire du responsable. La fiche indique leur emplacement. |
| **Clôture** | Une fiche n'est close qu'après réalisation du post-mortem et mise en œuvre des actions correctives. |
| **Revue** | Revue annuelle à l'occasion de l'[exercice de simulation](./breach-notification-procedure.md#13--exercice-de-simulation-annuel). La date de dernière mise à jour est actualisée même en l'absence de violation. |

---

## 3. Registre

### 3.1 Tableau de synthèse

| Réf. | Date de découverte (UTC) | Date de survenance (UTC) | Nature | Catégories de données concernées | Nb de personnes | Conséquences probables | Mesures prises | Notification autorité (O/N) + motif | Date de notification | Communication aux personnes (O/N) + motif | Date de clôture |
|---|---|---|---|---|---|---|---|---|---|---|---|
| *(aucune violation enregistrée à ce jour)* | | | | | | | | | | | |

### 3.2 Indicateurs

| Indicateur | Valeur |
|---|---|
| Nombre total de violations enregistrées | **0** |
| Dont notifiées à l'autorité de contrôle | **0** |
| Dont ayant donné lieu à une communication aux personnes | **0** |
| Délai moyen entre découverte et notification | *sans objet* |
| Nombre de violations notifiées au-delà de 72 heures | **0** |

---

## 4. Fiche détaillée — modèle

Une fiche détaillée est ouverte pour chaque entrée du tableau de synthèse, et placée à la suite du présent modèle.

---

### VIOL-AAAA-NN — *[titre court et factuel]*

#### Identification

| Champ | Valeur |
|---|---|
| Référence | `VIOL-AAAA-NN` |
| **Date et heure de découverte (UTC)** | |
| **Date et heure de survenance (UTC)** | *(estimée / confirmée)* |
| Durée d'exposition | |
| Statut de la violation | ☐ terminée ☐ en cours ☐ récurrente |
| Mode de découverte | ☐ alerte automatisée ☐ signalement externe ☐ constat interne ☐ notification du sous-traitant |
| Découvreur | |
| Responsable de l'incident | |
| **Échéance des 72 heures (UTC)** | |

#### Nature et cause

| Champ | Valeur |
|---|---|
| Type d'atteinte | ☐ confidentialité ☐ intégrité ☐ disponibilité |
| Origine | ☐ malveillance externe ☐ malveillance interne ☐ erreur humaine ☐ défaillance technique ☐ sous-traitant |
| Vecteur technique | |
| Description factuelle | |
| **Cause racine** | *(à compléter au post-mortem)* |

#### Périmètre

| Champ | Valeur |
|---|---|
| Catégories de données | ☐ identification (email, nom, prénom) ☐ authentification (empreintes, jetons) ☐ données de logement (adresse, équipements) ☐ données d'entretien (dates, coûts, prestataires, notes) ☐ données techniques (IP, agent utilisateur) ☐ journaux d'audit |
| Données de l'Art. 9 ou 10 | **Non** — aucune donnée sensible n'est traitée par le service |
| **Nombre approximatif de personnes** | |
| **Nombre approximatif d'enregistrements** | |
| Catégories de personnes | ☐ utilisateurs inscrits ☐ collaborateurs ☐ locataires ☐ prestataires cités |
| États membres de résidence | |
| Données protégées avant la violation | *(hachages BCrypt, SHA-256, jetons hachés — préciser lesquelles étaient en clair)* |

#### Qualification du risque

| Champ | Valeur |
|---|---|
| Grille appliquée | [§ 7 de la procédure](./breach-notification-procedure.md#7--qualification-du-risque) |
| Nature des données | |
| Volume | |
| Facilité d'identification | |
| Gravité des conséquences | |
| Protection préalable (Art. 34(3)(a)) | |
| Réversibilité | |
| **Niveau retenu** | ☐ improbable ☐ risque ☐ risque élevé |
| **Motivation** | |

#### Mesures

| Horodatage (UTC) | Mesure | Type | Opérateur |
|---|---|---|---|
| | | confinement / corrective / atténuation / préventive | |

#### Décisions

| Champ | Valeur |
|---|---|
| **Notification à l'autorité** | ☐ **Oui** ☐ **Non** |
| **Motivation de la décision** | *(obligatoire dans les deux cas)* |
| Autorité saisie | ☐ CNIL ☐ APD/GBA |
| Date et heure de notification (UTC) | |
| Délai respecté | ☐ oui ☐ non — motif du retard : |
| Numéro d'accusé de réception | |
| Notifications complémentaires | |
| **Communication aux personnes** | ☐ **Oui** ☐ **Non** |
| **Motivation** | *(si non : exception de l'Art. 34(3)(a), (b) ou (c) invoquée, et sa justification)* |
| Date de communication | |
| Canal utilisé | |
| Nombre de personnes touchées par la communication | |
| Sous-traitant impliqué | ☐ non ☐ oui : — notification reçue le : |

#### Preuves archivées

| Élément | Emplacement d'archivage |
|---|---|
| | |

#### Post-mortem

| Champ | Valeur |
|---|---|
| Date de réalisation | |
| Cause racine | |
| Ce qui a fonctionné | |
| Ce qui n'a pas fonctionné | |
| Actions correctives (responsable, échéance) | |
| Mises à jour de la documentation | |
| **Date de clôture** | |

---

## 5. Documents liés

- [Procédure de notification de violation de données](./breach-notification-procedure.md)
- [Index du dossier de conformité RGPD](../gdpr/README.md)
- [Registre des activités de traitement](../gdpr/processing-register.md)
- [Sous-traitants et destinataires](../gdpr/subprocessors.md)
- [SECURITY.md](../../SECURITY.md)
