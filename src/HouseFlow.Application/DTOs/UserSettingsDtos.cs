using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.DTOs;

public record UserSettingsDto(
    string Theme,
    string Language
);

public record UpdateUserSettingsDto(
    [Required]
    [RegularExpression("^(light|dark|system)$", ErrorMessage = "Theme must be 'light', 'dark', or 'system'")]
    string Theme,

    [Required]
    [RegularExpression("^(fr|en)$", ErrorMessage = "Language must be 'fr' or 'en'")]
    string Language
);
