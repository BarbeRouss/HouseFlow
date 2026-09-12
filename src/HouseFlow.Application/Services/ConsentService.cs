using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HouseFlow.Application.Services;

/// <inheritdoc cref="IConsentService" />
public class ConsentService : IConsentService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<ConsentService> _logger;

    public ConsentService(IApplicationDbContext context, ILogger<ConsentService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ConsentStatusDto> GetConsentStatusAsync(Guid userId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found");

        return ToStatus(user.ConsentGivenAt, user.ConsentPolicyVersion);
    }

    public async Task<ConsentStatusDto> RecordConsentAsync(Guid userId, bool accepted, string policyVersion, string? ipAddress = null)
    {
        if (!accepted)
            throw new InvalidOperationException("You must accept the terms of service to continue using your account");

        if (policyVersion != GdprPolicy.CurrentPolicyVersion)
            throw new InvalidOperationException($"Unknown policy version. The current version is {GdprPolicy.CurrentPolicyVersion}");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId)
            ?? throw new KeyNotFoundException("User not found");

        // L'audit trail enregistre l'IP et les nouvelles valeurs (preuve Art. 5(2)).
        _context.SetAuditContext(user.Id, user.Email, ipAddress);

        user.ConsentGivenAt = DateTime.UtcNow;
        user.ConsentPolicyVersion = GdprPolicy.CurrentPolicyVersion;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Terms accepted by user {UserId} (policy version {PolicyVersion})", user.Id, GdprPolicy.CurrentPolicyVersion);

        return ToStatus(user.ConsentGivenAt, user.ConsentPolicyVersion);
    }

    private static ConsentStatusDto ToStatus(DateTime? givenAt, string? version) => new(
        givenAt,
        version,
        GdprPolicy.IsConsentRequired(givenAt, version),
        GdprPolicy.CurrentPolicyVersion
    );
}
