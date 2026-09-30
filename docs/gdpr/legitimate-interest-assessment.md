# Test de mise en balance des intérêts légitimes (LIA)

**Article 6(1)(f) du RGPD — *Legitimate Interest Assessment***

| Élément | Valeur |
|---|---|
| **Responsable de traitement** | **Rouss Consulting SRL** (service HouseFlow) |
| **Contact vie privée** | `privacy@houseflow.cloud` |
| **Date de réalisation** | 2026-09-11 (§ 3 refait le 2026-09-28) |
| **Version** | 1.1 |
| **Prochaine revue** | annuelle, ou à toute évolution des traitements concernés |

---

## 0. Méthode

L'article 6(1)(f) autorise un traitement nécessaire aux fins des **intérêts légitimes** poursuivis par le responsable ou par un tiers, **à moins que ne prévalent les intérêts ou les libertés et droits fondamentaux** de la personne concernée.

Cette rédaction impose un test en **trois étapes cumulatives**, toutes les trois devant être franchies :

1. **Test de finalité** — l'intérêt poursuivi est-il *légitime*, réel, actuel et clairement articulé ?
2. **Test de nécessité** — le traitement est-il *nécessaire* pour atteindre cet intérêt ? Existe-t-il un moyen moins intrusif d'y parvenir aussi efficacement ?
3. **Test de mise en balance** — les intérêts, libertés et droits fondamentaux de la personne *prévalent-ils* sur l'intérêt du responsable, compte tenu de ses attentes raisonnables, de l'impact du traitement et des mesures d'atténuation en place ?

Ce document est un élément d'**accountability** (Art. 5(2)) : il doit pouvoir être produit lors d'un contrôle, et il fonde la mention des **intérêts légitimes poursuivis** exigée par l'**Art. 13(1)(d)** dans la politique de confidentialité.

**Rappel — droit d'opposition.** Tout traitement fondé sur l'article 6(1)(f) ouvre à la personne concernée un **droit d'opposition** (Art. 21(1)) pour des raisons tenant à sa situation particulière. Le responsable ne peut poursuivre le traitement que s'il démontre des **motifs légitimes et impérieux** prévalant sur les intérêts de la personne, ou pour la constatation, l'exercice ou la défense de droits en justice. Chaque conclusion ci-dessous précise la position retenue en cas d'opposition. Les modalités de réponse sont décrites dans le [journal des demandes d'exercice de droits](./rights-requests-log.md).

**Périmètre.** Trois traitements de HouseFlow reposent en tout ou partie sur l'intérêt légitime :

| § | Traitement | Fiche du registre |
|---|---|---|
| [1](#1--journaux-daudit) | Journaux d'audit | [TR-04](./processing-register.md#traitement-n-4--sécurité-journalisation-et-traçabilité) |
| [2](#2--refresh-tokens-adresses-ip-et-limitation-de-débit) | Refresh tokens, adresses IP et limitation de débit | [TR-04](./processing-register.md#traitement-n-4--sécurité-journalisation-et-traçabilité) |
| [3](#3--invitations-de-partage) | Invitations de partage | [TR-03](./processing-register.md#traitement-n-3--collaboration-et-invitations) |

---

## 1 — Journaux d'audit

### 1.1 Description du traitement

Chaque création, modification ou suppression d'une entité de la base est automatiquement consignée dans la table `AuditLogs` par le `SaveChangesAsync` du `HouseFlowDbContext`. L'entrée comporte : le type et l'identifiant de l'entité, l'action, l'identifiant et le nom d'utilisateur de l'auteur, l'horodatage, les valeurs avant et après la modification, la liste des propriétés modifiées, l'adresse IP et l'agent utilisateur.

### 1.2 Étape 1 — Test de finalité

| Question | Réponse |
|---|---|
| **Quel est l'intérêt poursuivi ?** | Garantir la **sécurité et l'intégrité** du service et des données confiées par les utilisateurs : détecter les accès non autorisés et les modifications anormales, reconstituer la chronologie d'un incident, identifier la cause d'une altération ou d'une disparition de données, et disposer d'une preuve des opérations en cas de litige entre membres d'une maison partagée. |
| **Cet intérêt est-il légitime ?** | **Oui.** Le **considérant 49** du RGPD reconnaît explicitement que « le traitement de données à caractère personnel dans la mesure strictement nécessaire et proportionnée aux fins de garantir la sécurité du réseau et des informations […] constitue un intérêt légitime ». La sécurité est en outre une **obligation** au titre de l'**Art. 32**, et la capacité à qualifier une violation dans les 72 heures une obligation au titre de l'**Art. 33** : sans piste d'audit, ces obligations sont matériellement inexécutables. |
| **Cet intérêt est-il réel et actuel ?** | **Oui.** Le service héberge des données de biens immobiliers (adresses, équipements, historiques d'intervention, coûts) partagées entre plusieurs personnes aux rôles distincts. Le risque de modification ou de suppression abusive par un membre, comme le risque de compromission d'un compte, sont concrets. |
| **Un tiers en bénéficie-t-il ?** | **Oui** — au premier chef les **utilisateurs eux-mêmes**, qui sont les bénéficiaires directs de la sécurité du service et les destinataires d'une éventuelle notification de violation. |

**Conclusion étape 1 : test franchi.**

### 1.3 Étape 2 — Test de nécessité

| Question | Réponse |
|---|---|
| **Le traitement est-il nécessaire ?** | **Oui.** Une trace d'audit sans auteur identifié est inexploitable : on saurait qu'une donnée a été modifiée, sans pouvoir déterminer si l'auteur est le titulaire légitime, un autre membre de la maison ou un attaquant. Or c'est précisément cette distinction qui fonde la qualification d'un incident. |
| **Existe-t-il un moyen moins intrusif ?** | Les alternatives ont été examinées et écartées : (i) *journalisation anonyme* — inexploitable, elle ne permet ni d'imputer une action, ni de notifier les personnes affectées (Art. 34) ; (ii) *journalisation des seules connexions* — insuffisante, elle ne couvre pas les modifications de données, qui sont le principal risque en environnement partagé ; (iii) *pseudonymisation immédiate de l'identifiant* — équivaut à l'anonymisation pour l'usage visé, puisque toute investigation exigerait de rétablir le lien. |
| **La collecte est-elle minimisée ?** | **Oui.** Les secrets (`PasswordHash`, `Token` de rafraîchissement, `KeyHash`) sont **exclus** de `OldValues` / `NewValues`. Les journaux applicatifs Serilog ne contiennent aucune donnée personnelle. Seules les propriétés effectivement modifiées sont consignées lors d'une mise à jour. |
| **La durée est-elle nécessaire ?** | **Oui, et volontairement bornée.** 1 an sous forme identifiante — durée retenue au sein de la fourchette de 6 mois à 1 an recommandée par la CNIL dans sa recommandation relative aux mesures de journalisation. Cette durée correspond à la réalité opérationnelle : une intrusion ou un abus interne est fréquemment découvert plusieurs mois après les faits, et une contestation entre membres d'une maison porte typiquement sur un cycle d'entretien annuel. |

**Conclusion étape 2 : test franchi.**

### 1.4 Étape 3 — Test de mise en balance

#### Impact sur la personne

| Critère | Appréciation |
|---|---|
| **Nature des données** | Données d'identification (identifiant, email), données techniques (IP, agent utilisateur) et contenu des modifications. **Aucune donnée de l'article 9.** L'adresse IP est une donnée personnelle (CJUE, 19 oct. 2016, *Breyer*, C-582/14). |
| **Volume et sensibilité** | Modéré. Les valeurs consignées sont celles que la personne a elle-même saisies dans le service ; le traitement ne crée aucune information nouvelle à son sujet. |
| **Statut des personnes** | Utilisateurs adultes, dans une relation contractuelle volontaire. Aucune personne vulnérable ciblée. |
| **Attentes raisonnables** | **Élevées et favorables.** Un utilisateur d'un service en ligne partagé s'attend à ce que les opérations soient tracées — c'est même, dans un contexte de maison partagée entre propriétaire, collaborateurs et locataires, une **demande** récurrente (« qui a supprimé cet entretien ? »). Le traitement est en outre explicitement annoncé dans la politique de confidentialité, ce qui exclut tout effet de surprise (considérant 47). |
| **Conséquences négatives possibles** | Faibles. Le principal risque résiduel est que les journaux soient eux-mêmes exposés lors d'une violation — risque traité par les mesures ci-dessous. Aucune décision défavorable, aucune exclusion, aucun profilage ne découle du traitement. |
| **Le traitement est-il intrusif ?** | **Non.** Il ne s'agit pas d'une surveillance comportementale : aucune analyse des habitudes, aucune inférence, aucune corrélation avec des sources externes, aucun usage commercial. Les journaux ne sont consultés que **sur incident** ou sur demande d'accès de la personne. |

#### Mesures d'atténuation en place

| Mesure | Effet |
|---|---|
| **Durée strictement bornée** | 1 an sous forme identifiante, puis **anonymisation** ; purge définitive à 3 ans. Application automatique par `DataRetentionJob` (quotidien, 03:00 UTC) — la durée est réellement appliquée en base, pas seulement annoncée. |
| **Troncature des adresses IP à 30 jours** | Au-delà de 30 jours, l'IP est tronquée par `IpAddressAnonymizer` (dernier octet en IPv4, 80 derniers bits en IPv6). L'investigation « à chaud » reste possible, la géolocalisation fine à froid ne l'est plus. |
| **Anonymisation robuste à 1 an** | Conformément à l'avis **WP216** du G29, l'anonymisation ne se limite pas à tronquer l'IP : `UserId`, `Username`, `IpAddress`, `UserAgent`, `OldValues`, `NewValues` et `ChangedProperties` sont **tous effacés**, ce qui rompt simultanément l'individualisation, la corrélation et l'inférence. |
| **Exclusion des secrets** | `PasswordHash`, `Token` et `KeyHash` ne sont jamais consignés — une fuite des journaux ne permettrait ni d'usurper une session ni de casser un mot de passe. |
| **Accès restreint** | Aucun accès utilisateur à la table `AuditLogs`. Accès administrateur limité au réseau privé (VNet), via bastion, sous authentification Entra ID sans mot de passe, selon le besoin d'en connaître. |
| **Transparence** | Traitement décrit dans la politique de confidentialité, avec l'intérêt légitime explicité (Art. 13(1)(d)) et la durée par catégorie (Art. 13(2)(a)). |
| **Droit d'accès effectif** | Les entrées d'audit concernant la personne figurent dans son export de données (Art. 15). |
| **Anonymisation à la suppression de compte** | La suppression de compte déclenche immédiatement l'anonymisation des journaux la concernant, sans attendre l'échéance d'un an. |

#### Balance

Les intérêts, libertés et droits fondamentaux de la personne **ne prévalent pas** sur l'intérêt légitime poursuivi. L'intérêt est de premier ordre — la sécurité des données des utilisateurs eux-mêmes — et il rejoint une obligation légale du responsable (Art. 32 et 33). L'impact est faible, conforme aux attentes raisonnables, et neutralisé à échéance par une anonymisation effective. Aucune alternative moins intrusive n'atteint le résultat recherché.

### 1.5 Conclusion

**Le traitement des journaux d'audit est licite sur le fondement de l'article 6(1)(f).**

**Position en cas d'opposition (Art. 21(1)).** La demande sera examinée individuellement et recevra une **réponse motivée par écrit** dans le délai d'un mois. L'issue attendue est le **refus**, fondé sur des motifs légitimes et impérieux : la sécurité du service et des données de l'ensemble des utilisateurs, l'exécution de l'obligation de l'article 32, et la constitution de preuves en cas de litige (Art. 21(1) *in fine*). La personne sera informée de ce motif ainsi que de son droit d'introduire une réclamation auprès d'une autorité de contrôle (Art. 12(4), 77). **La demande sera en revanche accueillie** si l'opposition porte sur une donnée dont la conservation n'est pas nécessaire à la finalité de sécurité : une troncature anticipée de l'IP sera alors mise en œuvre.

---

## 2 — Refresh tokens, adresses IP et limitation de débit

### 2.1 Description du traitement

- **Refresh tokens** (`RefreshTokens`) : jeton de 64 octets d'aléa cryptographique, stocké haché, valable 24 h (365 jours glissants avec « Se souvenir de moi »), accompagné de l'IP de création (`CreatedByIp`), de l'IP de révocation (`RevokedByIp`), du motif de révocation et de la référence au jeton remplaçant. Rotation systématique à chaque rafraîchissement.
- **Clés API** (`ApiKeys`) : hachage SHA-256, préfixe d'identification, IP de création, date de dernière utilisation.
- **Limitation de débit** : compteurs par adresse IP du client (préfixe /64 en IPv6), **en mémoire volatile uniquement**, jamais persistés — 5 requêtes/minute sur la connexion et l'inscription, 60/minute sur le rafraîchissement de session et la déconnexion, 200/minute en garde-fou global, dans les environnements Azure (production et previews).

### 2.2 Étape 1 — Test de finalité

| Question | Réponse |
|---|---|
| **Quel est l'intérêt poursuivi ?** | Deux intérêts distincts. **(i) Maintien de session** : éviter à l'utilisateur de ressaisir ses identifiants toutes les 15 minutes, durée de vie du JWT d'accès — cet aspect relève d'ailleurs aussi de l'**Art. 6(1)(b)**, la persistance de session étant une composante attendue du service. **(ii) Sécurité** : détecter le vol de session (rafraîchissement depuis une IP inattendue), permettre à l'utilisateur et à l'éditeur de révoquer des sessions, et protéger les comptes contre le bourrage d'identifiants et l'abus d'API. |
| **Cet intérêt est-il légitime ?** | **Oui.** Considérant 49 (sécurité des réseaux et de l'information) et Art. 32(1)(b) (garantir la confidentialité et l'intégrité constantes des systèmes). La protection contre le bourrage d'identifiants figure parmi les mesures explicitement attendues par la CNIL. |
| **Cet intérêt est-il réel et actuel ?** | **Oui.** Les attaques automatisées par bourrage d'identifiants constituent la menace la plus courante contre les services d'authentification exposés sur Internet ; le vol de jeton de session en est le corollaire immédiat. |

**Conclusion étape 1 : test franchi.**

### 2.3 Étape 2 — Test de nécessité

| Question | Réponse |
|---|---|
| **Le traitement est-il nécessaire ?** | **Oui** pour chacune de ses composantes. Le **jeton** est par construction indispensable au maintien de la session. L'**IP de création et de révocation** est le seul élément permettant à l'utilisateur, lorsqu'il consulte ses sessions actives, de reconnaître une session qui n'est pas la sienne — et à l'éditeur de caractériser un vol de jeton. Le **compteur par IP** est indispensable à toute limitation de débit : sans clé de partitionnement, la limitation ne peut pas exister. |
| **Existe-t-il un moyen moins intrusif ?** | (i) *Limitation par compte plutôt que par IP* — insuffisante, elle laisse passer les attaques distribuées visant de nombreux comptes ; elle permettrait de surcroît à un attaquant de verrouiller le compte d'un tiers (déni de service). (ii) *Renoncer à stocker l'IP du jeton* — priverait l'utilisateur de tout moyen de reconnaître une session illégitime et l'éditeur de tout moyen de qualifier une violation. (iii) *Sessions serveur classiques* — déplacerait le problème sans le supprimer, une session serveur exigeant les mêmes métadonnées. |
| **La collecte est-elle minimisée ?** | **Oui.** Les compteurs de limitation de débit sont **volatils** : aucune persistance en base, aucun historique des IP refusées. Les jetons sont stockés **hachés** : une fuite de la base ne permet pas de forger une session. La clé API en clair n'existe qu'au moment de sa création. Au plus 10 sessions actives sont conservées par utilisateur, la moins récemment utilisée étant évincée. |
| **La durée est-elle nécessaire ?** | **Oui.** Le jeton révoqué ou expiré n'est conservé que 30 jours — le temps d'investiguer un incident signalé tardivement et de détecter une tentative de réutilisation de jeton révoqué. Au-delà, il n'a plus aucune utilité. |

**Conclusion étape 2 : test franchi.**

### 2.4 Étape 3 — Test de mise en balance

#### Impact sur la personne

| Critère | Appréciation |
|---|---|
| **Nature des données** | Jeton opaque haché (sans signification en soi), adresse IP, horodatages. L'IP est une donnée personnelle (*Breyer*) et peut révéler une localisation approximative et un fournisseur d'accès. |
| **Volume** | Très faible : au plus 5 lignes de refresh token par utilisateur, plus les clés API qu'il crée lui-même. |
| **Attentes raisonnables** | **Élevées.** Rester connecté est une attente explicite de tout utilisateur ; voir ses sessions actives et pouvoir les révoquer est une fonctionnalité perçue comme protectrice. La limitation de débit est invisible pour un usage normal. |
| **Conséquences négatives possibles** | **(i)** Un utilisateur légitime pourrait être temporairement bloqué par la limitation de débit — impact limité, temporaire (fenêtre d'une minute), avec un message d'erreur explicite indiquant le délai d'attente. **(ii)** La conservation d'IP permet une localisation grossière — atténuée par la troncature à 30 jours. |
| **Le traitement est-il intrusif ?** | **Non.** Aucun suivi de navigation, aucune corrélation entre comptes, aucun usage des IP à des fins autres que la sécurité. |

#### Mesures d'atténuation en place

| Mesure | Effet |
|---|---|
| **Jetons hachés en base** | Une fuite de la base ne permet ni de forger ni de rejouer une session (Art. 32(1)(a)). |
| **Rotation avec révocation du jeton remplacé** | Un jeton volé devient inutilisable dès le premier rafraîchissement légitime, et la tentative de réutilisation est détectable. |
| **Cookie `HttpOnly` / `Secure` / `SameSite=Lax` / `Path=/api/v1/auth`** | Jeton inaccessible au JavaScript (protection XSS), transmis uniquement en HTTPS et uniquement aux routes d'authentification. |
| **Durée de vie courte** | JWT d'accès : 15 minutes. Refresh token : 24 h, ou 365 jours glissants si « Se souvenir de moi » est coché. Purge des jetons révoqués ou expirés : 30 jours. |
| **Troncature des IP à 30 jours** | Application automatique par `DataRetentionJob` via `IpAddressAnonymizer`. |
| **Compteurs volatils** | Les IP traitées pour la limitation de débit ne sont **jamais écrites sur disque** — aucune donnée résiduelle. |
| **Révocation en libre-service** | L'utilisateur peut révoquer ses sessions et ses clés API depuis son compte ; la suppression de compte les révoque toutes. |
| **Révocation de masse** | En cas d'incident, la commande `dotnet HouseFlow.API.dll --revoke-all-sessions` révoque l'intégralité des sessions et des clés API — mesure protectrice pour l'ensemble des personnes concernées. |

#### Balance

Les intérêts de la personne **ne prévalent pas**. Le traitement est minimal, essentiellement volatil ou à durée très courte, et opère **au bénéfice direct** de la personne — la protection de son compte. Les attentes raisonnables y sont pleinement satisfaites.

### 2.5 Conclusion

**Le traitement des refresh tokens, des adresses IP associées et des compteurs de limitation de débit est licite sur le fondement de l'article 6(1)(f)**, le maintien de session relevant de surcroît de l'article 6(1)(b).

**Position en cas d'opposition (Art. 21(1)).** L'opposition au traitement du jeton lui-même est **sans objet** : y faire droit reviendrait à supprimer la session, ce que l'utilisateur obtient déjà par la déconnexion. L'opposition à la conservation de l'IP recevra une **réponse motivée** ; l'issue attendue est le **refus** sur le fondement des motifs légitimes et impérieux de sécurité, mais une **troncature anticipée** de l'IP associée aux jetons de la personne sera mise en œuvre à sa demande, la finalité de sécurité restant alors atteinte de façon acceptable.

---

## 3 — Invitations de partage

### 3.1 Description du traitement

Un utilisateur peut partager une maison en créant une **invitation** : une ligne `Invitations` portant l'**adresse e-mail de la personne invitée** (saisie par l'invitant), un jeton opaque aléatoire, un rôle proposé, un statut et une date d'expiration. Le propriétaire invite tout rôle ; un collaborateur RW ne peut inviter qu'un locataire (règle R5). L'utilisateur transmet le lien porteur du jeton par le canal de son choix, **hors du service** : **aucun e-mail n'est envoyé**. La personne qui ouvre le lien voit le nom de la maison, le rôle proposé, le prénom et nom de l'invitant et, tant que l'invitation est utilisable, l'adresse e-mail invitée ; elle accepte ou refuse en se connectant ou en créant un compte avec cette adresse.

> **Donnée d'un tiers (depuis le 2026-09-27).** L'adresse e-mail de la personne invitée est la seule donnée d'un tiers **non inscrit** traitée au titre des invitations. Elle ne sert qu'à (1) **réserver l'invitation** à la personne désignée — l'inscription par le lien est verrouillée sur cette adresse, et seul le compte qui la porte peut accepter ou refuser —, (2) éviter une double invitation de la même adresse, (3) permettre au propriétaire (et, pour une invitation de locataire, au collaborateur RW) d'identifier l'invitation en attente. Collectée indirectement, elle appelle l'**information de l'Art. 14** : aucun e-mail n'étant envoyé, celle-ci est donnée au **premier contact**, sur la page du lien d'invitation (mention `invitations.privacyNotice`, avec renvoi à la politique de confidentialité, § 14).
>
> *Toute évolution introduisant l'**envoi** d'invitations par e-mail exigerait de reprendre ce test (sous-traitant d'emailing, information Art. 14 dans le corps du message).*

### 3.2 Étape 1 — Test de finalité

| Question | Réponse |
|---|---|
| **Quel est l'intérêt poursuivi ?** | Permettre le **partage d'une maison**, fonctionnalité centrale du service : un logement est habituellement géré à plusieurs (conjoints, copropriétaires, locataire et bailleur, gestionnaire). L'intérêt est partagé entre l'éditeur (fournir une fonctionnalité attendue) et l'utilisateur invitant (exercer le partage qu'il demande). |
| **Cet intérêt est-il légitime ?** | **Oui.** Il s'agit d'une fonctionnalité explicitement demandée par l'utilisateur, sans finalité détournée. Le mécanisme de jeton à durée limitée poursuit en outre un intérêt de sécurité : subordonner l'accès à une maison à un secret révocable et expirant. |
| **Cet intérêt est-il réel et actuel ?** | **Oui.** La gestion partagée d'un logement est un cas d'usage structurant du produit, reflété par les quatre rôles implémentés. |

**Conclusion étape 1 : test franchi.**

### 3.3 Étape 2 — Test de nécessité

| Question | Réponse |
|---|---|
| **Le traitement est-il nécessaire ?** | **Oui.** Il faut matérialiser, entre l'émission et l'acceptation, une proposition d'accès opposable : quel rôle, sur quelle maison, proposé par qui, jusqu'à quand. L'**adresse e-mail de la personne invitée** est nécessaire pour attacher cette proposition à une personne : sans elle, quiconque obtient le lien — transféré, intercepté, publié par erreur — pourrait rejoindre la maison ; avec elle, seul le compte portant l'adresse désignée peut accepter, ce qui prévient le détournement d'une invitation. |
| **Existe-t-il un moyen moins intrusif ?** | Un lien seul (sans adresse) ne permet pas de réserver l'invitation à son destinataire. Le dispositif retenu en reste au minimum : **une seule donnée** (l'adresse e-mail), **aucun envoi d'e-mail** — donc aucun sous-traitant d'emailing ni contact non sollicité — l'utilisateur invitant gardant la maîtrise du canal par lequel il transmet le lien. |
| **La divulgation est-elle minimisée ?** | **Oui.** Le porteur du jeton ne voit que le **nom de la maison**, le **rôle proposé**, les **prénom et nom de l'invitant** et, **tant que l'invitation est utilisable** seulement, l'**adresse e-mail invitée** (pour pré-remplir et verrouiller l'inscription) — soit le strict nécessaire pour décider d'accepter en connaissance de cause. L'adresse n'est plus renvoyée une fois l'invitation acceptée, refusée, annulée ou expirée ; elle n'est ni recopiée dans le journal d'audit, ni exportée dans l'export Art. 15/20 de l'invitant, et elle est pseudonymisée hors production. Aucune adresse, aucun équipement, aucun historique d'intervention, aucun coût et aucune information sur les autres membres ne sont exposés avant l'acceptation. |
| **La durée est-elle nécessaire ?** | **Oui.** Jeton valable 7 jours (« Renvoyer » régénère le jeton et repousse l'expiration de 7 jours), puis marqué `Expired` par le job quotidien ; l'invitation, **adresse e-mail comprise**, est **supprimée définitivement 30 jours après expiration**, quel que soit son statut — le temps de tracer un partage contesté. |

**Conclusion étape 2 : test franchi.**

### 3.4 Étape 3 — Test de mise en balance

#### Impact sur les personnes

Trois catégories de personnes sont affectées, à des degrés distincts.

| Personne | Données traitées | Impact |
|---|---|---|
| **Utilisateur invitant** | `CreatedByUserId` ; ses prénom et nom sont montrés au porteur du lien. | **Très faible** — il est à l'origine de l'action et connaît le destinataire, à qui il transmet lui-même le lien. |
| **Personne invitée** | **Avant acceptation : son adresse e-mail**, saisie par l'invitant. Après acceptation : `AcceptedByUserId` et une ligne `HouseMembers`. | **Faible** — une seule donnée de contact, communiquée par une personne qu'elle connaît, jamais utilisée pour la contacter, visible seulement du propriétaire (et des collaborateurs RW pour une invitation de locataire) et du porteur du lien tant qu'il est utilisable, puis effacée ; l'acceptation est un acte volontaire, et le traitement bascule alors sur la base contractuelle (Art. 6(1)(b)). |
| **Autres membres de la maison** | Un nouvel arrivant accédera aux données de la maison, qui peuvent refléter leur activité. | **Modéré** — c'est l'impact principal du traitement, traité par les mesures ci-dessous. |

| Critère | Appréciation |
|---|---|
| **Attentes raisonnables** | **Satisfaites.** L'invitant agit délibérément. L'invité reçoit un lien d'une personne qu'il connaît, dans un contexte qui lui est explicite ; qu'un service de partage conserve l'adresse à laquelle une invitation est destinée, le temps de sa validité, est prévisible, et la page d'invitation le lui dit dès l'ouverture du lien. Les autres membres ont adhéré à un service dont le partage est la fonction annoncée. |
| **Conséquences négatives possibles** | **(i)** Un lien transmis par erreur ou intercepté ne donne plus accès à la maison, l'acceptation étant réservée au compte portant l'adresse invitée ; il révèle en revanche cette adresse à son porteur tant que l'invitation est utilisable — atténué par l'expiration, la révocabilité et l'entropie du jeton. **(iii)** Une adresse saisie par erreur ou sans l'accord de la personne — atténué par l'absence d'envoi (la personne n'est pas démarchée), l'effacement automatique et l'obligation d'information de l'invitant (politique § 14). **(ii)** L'arrivée d'un membre élargit le cercle des personnes voyant les données de la maison — atténuée par les rôles et permissions. |
| **Le traitement est-il intrusif ?** | **Peu.** Une adresse e-mail n'est pas une donnée sensible ; elle n'est ni enrichie, ni croisée, ni utilisée pour contacter la personne, et celle-ci est informée au premier contact. |

#### Mesures d'atténuation en place

| Mesure | Effet |
|---|---|
| **Aucun e-mail envoyé** | L'adresse invitée n'est jamais utilisée pour contacter la personne : ni message non sollicité, ni sous-traitant d'emailing. |
| **Adresse montrée seulement tant qu'elle sert** | La page du lien ne renvoie l'adresse invitée que tant que l'invitation est utilisable (`HouseMemberService.GetInvitationInfoAsync`) ; la liste des invitations n'est visible que du propriétaire (et des collaborateurs RW pour les invitations de locataire). |
| **Exclusion de l'audit, pseudonymisation** | L'adresse invitée n'est pas recopiée dans le journal d'audit (`SensitiveAuditProperties`), ni dans l'export de l'invitant ; elle est pseudonymisée hors production (`dbtools/pseudonymize.sql`, contrôle `verify.sql`). |
| **Information Art. 14 au premier contact** | Mention `invitations.privacyNotice` sur la page du lien, pour la personne connectée comme non connectée, avec renvoi à la politique de confidentialité (§ 14). |
| **Jeton opaque et aléatoire, index unique** | Un lien d'invitation n'est ni devinable ni énumérable. |
| **Expiration automatique** | Job quotidien `DataRetentionJob` (règle invitations) : marquage `Expired` à l'échéance, suppression définitive — adresse e-mail comprise — 30 jours plus tard. |
| **Révocation et refus à tout moment** | Le propriétaire (ou un collaborateur RW, pour une invitation de locataire) peut annuler une invitation avant son acceptation ; la personne invitée peut la refuser. |
| **Divulgation minimale avant acceptation** | Nom de la maison, rôle, identité de l'invitant et adresse invitée tant que l'invitation est utilisable — rien d'autre. |
| **Rôles et permissions granulaires** | Un membre invité n'accède qu'au périmètre de son rôle ; l'accès aux **coûts** requiert la permission distincte `CanViewCosts`, **fausse par défaut** — application concrète du *privacy by default* (Art. 25(2)). |
| **Retrait d'un membre** | Le propriétaire peut retirer à tout moment un membre, ce qui supprime son accès et son adhésion. |
| **Transparence** | Le mécanisme d'invitation et ses conséquences sont décrits dans la politique de confidentialité. |

#### Balance

Les intérêts des personnes **ne prévalent pas**. Le traitement est déclenché par l'utilisateur lui-même et conforme aux attentes de toutes les parties. La seule donnée d'un tiers non inscrit — son adresse e-mail — est nécessaire pour réserver l'invitation à son destinataire et en prévenir le détournement ; elle n'est jamais utilisée pour le contacter, n'est montrée que tant qu'elle sert, est exclue de l'audit, pseudonymisée hors production et effacée 30 jours après l'expiration, et la personne en est informée dès son premier contact avec le service. L'impact résiduel — l'élargissement du cercle d'accès à une maison — est inhérent à la fonctionnalité de partage souscrite et encadré par les rôles, les permissions et la réversibilité.

### 3.5 Conclusion

**Le traitement lié à l'émission des invitations est licite sur le fondement de l'article 6(1)(f).** Une fois l'invitation acceptée, la relation avec le nouveau membre repose sur l'**article 6(1)(b)** (exécution du contrat).

**Position en cas d'opposition (Art. 21(1)).** L'opposition sera **accueillie** : à la demande d'une personne, l'invitation concernée est révoquée et l'adhésion correspondante supprimée ; la personne invitée peut aussi refuser elle-même l'invitation depuis la page du lien. Son adresse e-mail est alors effacée à l'échéance de purge, ou immédiatement à sa demande (Art. 17). Aucun motif légitime impérieux ne justifierait de maintenir un partage contre la volonté de la personne — à la seule réserve des adhésions nécessaires à l'exécution du contrat des autres membres, et de la conservation de la trace d'audit du partage jusqu'à son échéance de purge.

---

## 4 — Synthèse

| Traitement | Étape 1 : finalité | Étape 2 : nécessité | Étape 3 : balance | Conclusion | Suite donnée à une opposition |
|---|:---:|:---:|:---:|---|---|
| Journaux d'audit | ✅ | ✅ | ✅ | **Licite** — Art. 6(1)(f) | Refus motivé (motifs légitimes impérieux : sécurité, Art. 32, preuve) ; troncature anticipée de l'IP acceptée |
| Refresh tokens, IP, limitation de débit | ✅ | ✅ | ✅ | **Licite** — Art. 6(1)(f), + Art. 6(1)(b) pour le maintien de session | Refus motivé pour le jeton ; troncature anticipée de l'IP acceptée |
| Invitations de partage | ✅ | ✅ | ✅ | **Licite** — Art. 6(1)(f) ; Art. 6(1)(b) après acceptation | **Accueillie** : révocation de l'invitation, suppression de l'adhésion |

**Revue.** Ce test de mise en balance est réexaminé **annuellement** par le référent vie privée, et **immédiatement** en cas de : modification d'une durée de conservation, ajout d'une catégorie de données à l'un de ces traitements, introduction de l'envoi d'invitations par email, ou toute évolution vers une exploitation analytique ou comportementale des journaux.

---

## 5 — Documents liés

- [Registre des activités de traitement](./processing-register.md) — fiches TR-03 et TR-04
- [Politique de conservation des données](./data-retention-policy.md) — durées et mécanismes de purge
- [Journal des demandes d'exercice de droits](./rights-requests-log.md) — traitement des oppositions (Art. 21)
- [Index du dossier de conformité](./README.md)
