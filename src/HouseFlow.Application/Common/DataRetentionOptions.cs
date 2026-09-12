namespace HouseFlow.Application.Common;

/// <summary>
/// RGPD Art. 5(1)(e) — limitation de la conservation. Durées de conservation appliquées
/// automatiquement par <c>DataRetentionJob</c> (Infrastructure/Jobs). Chaque durée
/// annoncée dans la politique de confidentialité et le registre des traitements DOIT
/// correspondre exactement à la valeur configurée ici : la CNIL contrôle la durée
/// réellement appliquée en base, pas celle annoncée.
///
/// Liée à la section <c>DataRetention</c> de <c>appsettings.json</c> (le JSON n'acceptant
/// pas de commentaires, la justification de chaque durée est documentée ici).
/// </summary>
public class DataRetentionOptions
{
    public const string SectionName = "DataRetention";

    /// <summary>
    /// RGPD Art. 5(1)(c) — minimisation. Au-delà de ce délai, les adresses IP conservées
    /// pour la sécurité (journaux d'audit, refresh tokens <c>CreatedByIp</c>/<c>RevokedByIp</c>,
    /// <c>ApiKey.CreatedByIp</c>) sont tronquées via <see cref="IpAddressAnonymizer"/>.
    /// 30 jours = fenêtre d'investigation d'un incident (vol de token, accès anormal) ;
    /// au-delà, l'IP complète n'est plus nécessaire à la finalité de sécurité.
    /// </summary>
    public int IpAnonymizeAfterDays { get; set; } = 30;

    /// <summary>
    /// Suppression définitive des refresh tokens révoqués OU expirés depuis plus de N jours.
    /// Le token ne peut plus servir à authentifier ; seule la détection de réutilisation
    /// justifie de le garder quelque temps (Art. 6(1)(f) sécurité).
    /// </summary>
    public int RevokedRefreshTokenRetentionDays { get; set; } = 30;

    /// <summary>Suppression définitive des clés API révoquées depuis plus de N jours.</summary>
    public int RevokedApiKeyRetentionDays { get; set; } = 30;

    /// <summary>
    /// Au-delà de ce délai, un journal d'audit est anonymisé : <c>UserId</c>, <c>Username</c>,
    /// <c>IpAddress</c>, <c>UserAgent</c>, <c>OldValues</c>, <c>NewValues</c> et
    /// <c>ChangedProperties</c> sont mis à <c>null</c>. Seuls <c>EntityType</c>,
    /// <c>EntityId</c>, <c>Action</c> et <c>Timestamp</c> sont conservés pour les
    /// statistiques de sécurité. Le lien avec la personne étant rompu (plus
    /// d'individualisation ni de corrélation possibles, cf. WP216), l'enregistrement sort
    /// du champ du RGPD au sens du considérant 26.
    /// 1 an = borne haute de la recommandation CNIL sur les mesures de journalisation
    /// (6 mois à 1 an en base active).
    /// </summary>
    public int AuditLogAnonymizeAfterDays { get; set; } = 365;

    /// <summary>
    /// Suppression définitive des journaux d'audit (même anonymisés) au-delà de 3 ans —
    /// borne maximale admise par la CNIL pour un dispositif de contrôle interne justifié.
    /// </summary>
    public int AuditLogDeleteAfterDays { get; set; } = 1095;

    /// <summary>
    /// Purge définitive (hard-delete) des entités <c>ISoftDeletable</c> marquées supprimées
    /// depuis plus de N jours. Un soft-delete conservé indéfiniment n'est qu'une
    /// pseudonymisation : les données restent personnelles et le manquement à l'Art. 17
    /// et à l'Art. 5(1)(e) est constitué.
    /// </summary>
    public int SoftDeletedRetentionDays { get; set; } = 30;

    /// <summary>
    /// Suppression des invitations non acceptées / expirées / révoquées, N jours après
    /// leur date d'expiration.
    /// </summary>
    public int ExpiredInvitationRetentionDays { get; set; } = 30;

    /// <summary>
    /// Taille des lots de purge. Les suppressions et anonymisations sont effectuées par
    /// lots, en boucle jusqu'à épuisement, pour éviter les verrous longs en base.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Expression CRON (UTC) de la tâche récurrente Hangfire <c>data-retention</c>.
    /// Défaut : tous les jours à 03:00 UTC.
    /// </summary>
    public string Cron { get; set; } = "0 3 * * *";
}
