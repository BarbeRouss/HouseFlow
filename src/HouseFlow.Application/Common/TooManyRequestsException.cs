namespace HouseFlow.Application.Common;

/// <summary>
/// Levée quand une opération soumise à quota utilisateur est rejouée trop tôt
/// (ex. export RGPD limité à 1 par heure, Art. 12(5) — demandes manifestement
/// excessives). Mappée en HTTP 429 avec un header <c>Retry-After</c> par
/// <c>DomainExceptionFilter</c>.
/// </summary>
public class TooManyRequestsException : Exception
{
    /// <summary>Nombre de secondes à attendre avant de réessayer (header Retry-After).</summary>
    public int RetryAfterSeconds { get; }

    public TooManyRequestsException(int retryAfterSeconds, string? message = null)
        : base(message ?? "Too many requests. Please try again later.")
    {
        RetryAfterSeconds = retryAfterSeconds < 1 ? 1 : retryAfterSeconds;
    }
}
