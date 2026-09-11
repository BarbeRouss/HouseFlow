namespace HouseFlow.Application.DTOs;

// ConsentRequestDto → generated as HouseFlow.Contracts.ConsentRequest (see ContractAliases.cs)

/// <summary>
/// Statut d'acceptation des Conditions générales d'utilisation (contrat, RGPD Art. 6(1)(b))
/// et de prise de connaissance de la politique de confidentialité (information, Art. 13).
/// </summary>
/// <remarks>
/// Le nommage « consent » est historique (issues #135/#134) : la base légale du compte est
/// l'exécution du contrat, pas le consentement de l'Art. 6(1)(a). Les valeurs conservées
/// (date + version) servent de preuve d'accountability au titre de l'Art. 5(2).
/// </remarks>
public record ConsentStatusDto(
    DateTime? ConsentGivenAt,
    string? ConsentPolicyVersion,
    bool ConsentRequired,
    string CurrentPolicyVersion
);
