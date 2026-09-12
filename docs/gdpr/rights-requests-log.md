# Journal des demandes d'exercice de droits

**Articles 12 à 22 du RGPD — traitement des demandes et preuve de conformité (Art. 5(2))**

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | HouseFlow (éditeur : BarbeRouss) |
| **Référent vie privée** | `privacy@houseflow.app` |
| **Date de création** | 2026-09-11 |
| **Version** | 1.0 |
| **Durée de conservation de ce journal** | **3 ans** à compter de la réponse apportée |

> **Pourquoi ce journal existe.** L'article 5(2) exige que le responsable soit en mesure de **démontrer** le respect du règlement. Une réponse correctement apportée mais non tracée est, lors d'un contrôle, indistinguable d'une absence de réponse. Ce journal consigne donc **toute** demande, y compris celles auxquelles il a été fait droit immédiatement et celles qui ont été refusées.

---

## 1. Règles applicables à toute demande

| Règle | Fondement | Application chez HouseFlow |
|---|---|---|
| **Délai d'un mois** | Art. 12(3) | Le délai court **à compter de la réception** de la demande — **jamais** à compter de la vérification d'identité ni du début de l'instruction. Prorogeable de **deux mois** en raison de la complexité ou du nombre de demandes, à condition d'informer la personne **et de motiver la prorogation dans le premier mois**. |
| **Gratuité** | Art. 12(5) | Toute réponse est **gratuite**. Des frais raisonnables ou un refus ne sont envisageables que pour une demande manifestement infondée ou excessive, notamment répétitive — **la charge de la preuve du caractère excessif incombant au responsable**. |
| **Vérification d'identité proportionnée** | Art. 12(6) | Un utilisateur **authentifié dans l'application est réputé identifié** : aucune pièce complémentaire ne lui est demandée. Une pièce d'identité n'est réclamée qu'en cas de **doute raisonnable et motivé** sur l'identité du demandeur — typiquement une demande par email émanant d'une adresse non rattachée à un compte. **Réclamer systématiquement une copie de pièce d'identité est une pratique excessive, sanctionnée comme telle.** |
| **Motivation de tout refus** | Art. 12(4) | Un refus, même partiel, est notifié **par écrit dans le mois**, avec son motif, ainsi que le rappel du droit d'introduire une **réclamation auprès d'une autorité de contrôle** et d'exercer un **recours juridictionnel**. |
| **Notification aux destinataires** | Art. 19 | Toute rectification, tout effacement et toute limitation sont notifiés à chaque destinataire, sauf impossibilité ou effort disproportionné. **En pratique chez HouseFlow** : l'unique destinataire est le sous-traitant d'hébergement, sur lequel l'opération se propage automatiquement ; aucun système tiers n'a à être notifié. |
| **Rappel explicite du droit d'opposition** | Art. 21(4) | Le droit d'opposition est porté à la connaissance de la personne **explicitement et séparément** des autres informations, dans la politique de confidentialité. |

---

## 2. Traitement par droit

### 2.1 Droits couverts par le libre-service (réponse immédiate)

La majorité des demandes se règlent **sans intervention humaine**, ce qui constitue à la fois la meilleure expérience utilisateur et la meilleure preuve de conformité.

| Droit | Article | Mécanisme en libre-service | Délai réel |
|---|---|---|---|
| **Accès** | Art. 15 | `GET /api/v1/users/me/export?format=json\|csv` — l'export inclut un volet **métadonnées** reprenant les informations de l'Art. 15(1)(a) à (h) : finalités, catégories de données, destinataires, durées de conservation, droits, droit de réclamation, source des données, absence de décision automatisée. C'est ce volet qui distingue un export conforme à l'Art. 15 d'un simple vidage de données. | **Immédiat** |
| **Portabilité** | Art. 20 | Même endpoint. **JSON** (`UserDataExport`) par défaut ; **CSV** sous forme d'archive ZIP comportant un fichier par catégorie. Les deux formats satisfont le critère « structuré, couramment utilisé et lisible par machine ». Le périmètre est restreint aux données **fournies par la personne** et traitées sur base contractuelle : les journaux d'audit, les adresses IP et les jetons en sont **exclus** (données observées, base 6(1)(f)). | **Immédiat** |
| **Rectification** | Art. 16 | `PUT /api/v1/users/me` et écran « Mon profil » : prénom, nom, email, préférences. | **Immédiat** |
| **Effacement** | Art. 17 | `DELETE /api/v1/users/me` — suppression **immédiate et définitive**, sans période de grâce. Détail des opérations : [politique de conservation, § 4](./data-retention-policy.md#4-suppression-de-compte). | **Immédiat** |

**Garanties applicables à l'export** (Art. 15(4) et 20(4) — les droits d'autrui) :

- aucun secret n'est exporté : ni `PasswordHash`, ni `Token` de rafraîchissement, ni `KeyHash` ;
- aucune donnée identifiant un tiers n'est exportée : pour une maison partagée, seuls le **nom de la maison** et le **rôle** de la personne apparaissent — jamais les nom, prénom ou email des autres membres ;
- **une limite d'un export par heure et par utilisateur** protège contre l'abus ;
- chaque export est tracé par une entrée d'audit `Action = "DataExport"`.

**Ces demandes doivent malgré tout être consignées** dans le tableau du § 4 **lorsqu'elles parviennent par email** plutôt que par le libre-service. Une demande adressée à `privacy@houseflow.app` appelle une réponse écrite : il n'est **pas** conforme de répondre « c'est dans votre compte » sans fournir les informations de l'article 15(1) ni accompagner la personne.

### 2.2 Limitation du traitement (Art. 18) — procédure manuelle

L'article 18(1) ouvre le droit à la limitation dans **quatre cas** :

1. l'**exactitude** des données est contestée, le temps de la vérification ;
2. le traitement est **illicite** et la personne s'oppose à l'effacement en demandant la limitation ;
3. les données ne sont plus nécessaires au responsable, mais restent **nécessaires à la personne** pour constater, exercer ou défendre des droits en justice ;
4. une **opposition** au titre de l'Art. 21(1) est en cours d'examen.

Les données limitées ne peuvent alors qu'être **conservées** (Art. 18(2)), et la personne doit être **informée avant toute levée** de la limitation (Art. 18(3)).

**État actuel : aucun indicateur `ProcessingRestricted` n'est implémenté.** La limitation est mise en œuvre par une **procédure manuelle documentée**, ce qui est admis pour une structure de cette taille dès lors que la procédure existe réellement et qu'elle est traçable.

| Mesure concrètement réalisable aujourd'hui | Exécution |
|---|---|
| **Gel de l'accès au compte** | Mise à jour directe en base, via le bastion, du `PasswordHash` vers une valeur inerte non correspondante — la connexion devient impossible sans qu'aucune donnée ne soit supprimée. La valeur d'origine est conservée hors ligne par le référent vie privée pour permettre la levée de la limitation. |
| **Révocation de toutes les sessions** | Suppression des lignes `RefreshTokens` et `ApiKeys` de l'utilisateur — plus aucune session active, plus aucun accès par clé API. Le JWT d'accès résiduel expire en 15 minutes au plus. |
| **Gel des traitements non essentiels** | Aucun email marketing, aucune analyse, aucun profilage n'existant, **aucun traitement non essentiel n'est à suspendre** : le gel de l'accès et la révocation des sessions épuisent en pratique la portée de l'Art. 18(2). |
| **Suspension de la purge automatique** | Si la limitation vise à préserver des données pour une action en justice, l'exclusion des enregistrements concernés du champ du `DataRetentionJob` est appliquée manuellement, afin d'empêcher toute suppression automatique pendant la période de limitation. |
| **Marquage** | Consignation de la limitation dans le présent journal : date de début, motif, périmètre exact, date d'information de la personne. |

**Levée de la limitation.** La personne en est **informée préalablement** (Art. 18(3)). L'accès est restauré et la consignation complétée par la date et le motif de levée.

### 2.3 Opposition (Art. 21) — réponse motivée obligatoire

Le droit d'opposition ne s'exerce qu'à l'égard des traitements fondés sur l'**article 6(1)(f)** — chez HouseFlow : les **journaux d'audit**, les **refresh tokens et adresses IP**, et les **invitations**. Le responsable ne peut poursuivre le traitement que s'il démontre des **motifs légitimes et impérieux** prévalant sur les intérêts, droits et libertés de la personne, ou pour la constatation, l'exercice ou la défense de droits en justice.

| Traitement visé | Position retenue | Motivation à opposer |
|---|---|---|
| **Journaux d'audit** | **Refus motivé** (avec aménagement) | **Motifs légitimes et impérieux** : la sécurité du service et des données de l'ensemble des utilisateurs, l'exécution de l'obligation de l'Art. 32, la capacité à qualifier et notifier une violation dans les 72 heures (Art. 33), et la constitution d'une preuve en cas de litige entre membres d'une maison partagée. **Aménagement systématiquement proposé** : troncature anticipée de l'adresse IP associée aux entrées de la personne, la finalité de sécurité demeurant atteinte. La motivation complète figure dans le [LIA](./legitimate-interest-assessment.md#1--journaux-daudit). |
| **Refresh tokens et adresses IP** | **Refus motivé** pour le jeton, **accueil** pour l'IP | L'opposition au jeton est sans objet : la déconnexion produit déjà l'effet recherché. La troncature anticipée de l'IP est en revanche mise en œuvre à la demande. Voir le [LIA](./legitimate-interest-assessment.md#2--refresh-tokens-adresses-ip-et-limitation-de-débit). |
| **Invitations** | **Accueil de la demande** | Aucun motif impérieux ne justifie de maintenir un partage contre la volonté d'une personne : l'invitation est révoquée et l'adhésion supprimée. Voir le [LIA](./legitimate-interest-assessment.md#3--invitations-de-partage). |
| **Prospection commerciale** | **Sans objet** | HouseFlow ne réalise **aucune prospection**. Si une telle activité était introduite, l'opposition serait **absolue et inconditionnelle** (Art. 21(2)-(3)), avec prise d'effet immédiate et lien de désinscription dans chaque message. |

**Point de vigilance.** Un refus d'opposition **non motivé par écrit** constitue un manquement, quand bien même le refus serait fondé. Toute réponse doit énoncer le motif légitime impérieux invoqué, et rappeler le droit de réclamation auprès d'une autorité de contrôle.

**Ne jamais confondre opposition (Art. 21) et effacement (Art. 17).** Répondre à une opposition en supprimant le compte est une erreur de qualification : la personne n'a pas demandé à quitter le service.

### 2.4 Droits sans objet chez HouseFlow

| Droit | Article | Motif |
|---|---|---|
| **Retrait du consentement** | Art. 7(3) | **Aucun traitement n'est fondé sur le consentement** (Art. 6(1)(a)). La case cochée à l'inscription est une acceptation contractuelle des CGU, non un consentement au traitement. Une demande de « retrait de consentement » sera requalifiée et traitée comme une demande d'**effacement** (Art. 17) ou d'**opposition** (Art. 21), après explication écrite. |
| **Décision automatisée** | Art. 22 | Aucune décision automatisée, aucun profilage n'est mis en œuvre. |
| **Directives post-mortem** | Art. 85 de la loi Informatique et Libertés | Le service ne dispose pas de dispositif de recueil de directives. Une demande d'un héritier est traitée au cas par cas : vérification de la qualité d'héritier, puis clôture du compte selon la procédure d'effacement. |

---

## 3. Procédure de traitement d'une demande

| Étape | Action | Délai |
|---|---|---|
| **1. Réception** | Consigner **immédiatement** la demande au § 4 avec sa date de réception exacte. **Le délai d'un mois court à partir de cet instant.** | J |
| **2. Accusé de réception** | Confirmer la réception à la personne, préciser le délai de réponse et l'adresse de contact. | J+2 ouvrés |
| **3. Qualification** | Identifier le ou les droits réellement exercés — une demande est fréquemment rédigée sans référence à un article. Requalifier si nécessaire, et l'expliquer à la personne. | J+2 |
| **4. Vérification d'identité** | Si le demandeur est authentifié dans l'application : **aucune vérification supplémentaire**. Sinon, et en cas de **doute raisonnable** seulement, demander un élément proportionné (confirmation depuis l'adresse email du compte, par exemple). **Cette étape ne suspend pas le délai.** | J+5 |
| **5. Instruction** | Rassembler les données, appliquer les garanties relatives aux droits des tiers (Art. 15(4) et 20(4)), préparer la réponse. Pour une opposition : rédiger la motivation. | — |
| **6. Réponse** | Répondre par écrit. En cas de refus total ou partiel : motiver, et rappeler le droit de réclamation auprès d'une autorité de contrôle ainsi que le droit à un recours juridictionnel. | **≤ J+1 mois** |
| **7. Prorogation** *(si nécessaire)* | Informer la personne de la prorogation **et de ses motifs** avant l'expiration du premier mois. La prorogation ne peut excéder deux mois supplémentaires. | ≤ J+1 mois |
| **8. Clôture** | Compléter la ligne du journal : décision, motif, date de réponse. | — |

---

## 4. Journal des demandes

**Instructions de tenue.** Une ligne par demande. Consigner **toutes** les demandes, y compris celles satisfaites immédiatement et celles refusées. Ne pas consigner les usages du libre-service réalisés par l'utilisateur sans sollicitation du référent : ils sont tracés par la piste d'audit applicative (`DataExport`, `AccountDeleted`).

**Minimisation.** Ce journal est lui-même un traitement (fiche TR-05 du [registre](./processing-register.md#traitement-n-5--exercice-des-droits-rgpd)). Y consigner **le strict nécessaire** : identifier la personne par son identifiant de compte lorsqu'il existe, plutôt que par son adresse email. Ne **jamais** y joindre de copie de pièce d'identité.

| Réf. | Date de réception | Personne (identifiant) | Droit exercé | Canal | Identité vérifiée — comment | Décision | Motif (si refus ou refus partiel) | Date de réponse | Délai respecté | Opérateur |
|---|---|---|---|---|---|---|---|---|---|---|
| *(aucune demande enregistrée à ce jour)* | | | | | | | | | | |

### Valeurs normalisées

| Colonne | Valeurs admises |
|---|---|
| **Droit exercé** | Accès (15) · Rectification (16) · Effacement (17) · Limitation (18) · Portabilité (20) · Opposition (21) · Information (13-14) · Autre |
| **Canal** | Libre-service in-app · Email `privacy@houseflow.app` · Email `security@rouss.be` · Courrier postal · Via l'autorité de contrôle |
| **Identité vérifiée — comment** | Authentifié dans l'application · Confirmation depuis l'email du compte · Pièce justificative (à motiver) · Non applicable |
| **Décision** | Accueillie · Accueillie partiellement · Refusée · Requalifiée · Sans objet |
| **Délai respecté** | Oui · Non (à motiver) · Prorogé (motif et date d'information de la personne) |

---

## 5. Modèles de réponse

### 5.1 Accusé de réception

> Objet : Votre demande relative à vos données personnelles — accusé de réception
>
> Bonjour,
>
> Nous avons bien reçu, le [date], votre demande relative à l'exercice de vos droits sur vos données personnelles.
>
> Nous y répondrons **au plus tard le [date + 1 mois]**. Si la complexité de votre demande nous conduisait à avoir besoin de davantage de temps, nous vous en informerions avant cette échéance, en vous indiquant les raisons de ce délai supplémentaire.
>
> Cette démarche est entièrement gratuite.
>
> Nous vous rappelons que vous pouvez, à tout moment et directement depuis votre compte, consulter et exporter l'ensemble de vos données, corriger vos informations personnelles et supprimer définitivement votre compte.
>
> Pour toute question, vous pouvez répondre à ce message.
>
> L'équipe HouseFlow — `privacy@houseflow.app`

### 5.2 Refus motivé d'une opposition sur les journaux de sécurité

> Objet : Votre demande d'opposition — notre réponse
>
> Bonjour,
>
> Vous nous avez demandé, le [date], de cesser de conserver les journaux d'activité liés à votre compte.
>
> Après examen de votre situation, **nous ne pouvons pas faire droit à cette demande**, pour les raisons suivantes.
>
> Ces journaux enregistrent les créations, modifications et suppressions effectuées sur les données de votre compte. Ils poursuivent une finalité de sécurité : détecter un accès frauduleux, déterminer l'origine d'une modification anormale et, le cas échéant, vous alerter si vos données étaient exposées. Cette conservation répond à une obligation qui nous incombe au titre de l'article 32 du RGPD, et elle conditionne notre capacité à vous informer en cas d'incident, comme l'exige l'article 34. Ces journaux constituent enfin le seul moyen d'établir qui a modifié une donnée lorsqu'une maison est partagée entre plusieurs personnes.
>
> Ces motifs constituent, au sens de l'article 21(1) du RGPD, des motifs légitimes et impérieux prévalant sur votre demande.
>
> Nous prenons néanmoins deux mesures en votre faveur :
>
> - votre adresse IP, dans ces journaux, est **immédiatement tronquée** au lieu de l'être au bout de trente jours ;
> - ces journaux sont **automatiquement anonymisés au bout d'un an**, et ils le seront **immédiatement** si vous supprimez votre compte.
>
> Si vous estimez que cette réponse ne respecte pas vos droits, vous pouvez introduire une réclamation auprès d'une autorité de protection des données — en particulier celle de l'État membre où vous résidez — et exercer un recours juridictionnel :
>
> - **France — CNIL** : 3 Place de Fontenoy, TSA 80715, 75334 Paris Cedex 07 — [cnil.fr/fr/plaintes](https://www.cnil.fr/fr/plaintes)
> - **Belgique — Autorité de protection des données** : Rue de la Presse 35, 1000 Bruxelles — `contact@apd-gba.be`
>
> L'équipe HouseFlow — `privacy@houseflow.app`

### 5.3 Réponse à une demande d'accès formulée par email

> Objet : Votre demande d'accès à vos données personnelles
>
> Bonjour,
>
> Vous nous avez demandé, le [date], de vous communiquer les données personnelles que nous détenons à votre sujet.
>
> Vous trouverez **en pièce jointe** l'intégralité de ces données, au format [JSON / CSV]. Ce fichier comprend votre profil, vos maisons, vos équipements, vos types d'entretien et vos interventions, vos partages, vos sessions et les entrées de journal vous concernant.
>
> Il contient également une section « métadonnées » précisant, conformément à l'article 15 du RGPD : les finalités de chaque traitement, les catégories de données concernées, les destinataires, les durées de conservation, vos droits, votre droit d'introduire une réclamation auprès d'une autorité de contrôle, l'origine des données, et l'absence de toute décision automatisée ou de profilage.
>
> Deux précisions. Certaines informations relatives à d'autres personnes ont été écartées, afin de ne pas porter atteinte à leurs droits : pour une maison partagée, vous verrez le nom de la maison et votre rôle, mais pas l'identité des autres membres. Par ailleurs, les éléments secrets (empreinte de votre mot de passe, jetons de session, empreintes de vos clés d'API) ne sont pas communiqués : leur divulgation créerait un risque pour la sécurité de votre compte sans vous apporter d'information utile. Leur existence et leur finalité vous sont décrites dans notre politique de confidentialité.
>
> Vous pouvez à tout moment obtenir ce même export directement depuis votre compte.
>
> L'équipe HouseFlow — `privacy@houseflow.app`

---

## 6. Documents liés

- [Index du dossier de conformité](./README.md)
- [Registre des activités de traitement](./processing-register.md) — fiche TR-05
- [Test de mise en balance des intérêts légitimes (LIA)](./legitimate-interest-assessment.md) — motivation des refus d'opposition
- [Politique de conservation des données](./data-retention-policy.md) — effets de la suppression de compte
- [Procédure de notification de violation de données](../security/breach-notification-procedure.md)
