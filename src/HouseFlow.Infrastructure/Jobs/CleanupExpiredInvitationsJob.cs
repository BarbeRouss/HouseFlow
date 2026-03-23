using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HouseFlow.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job that marks expired invitations and deletes old ones.
/// </summary>
public class CleanupExpiredInvitationsJob
{
    private readonly HouseFlowDbContext _context;
    private readonly ILogger<CleanupExpiredInvitationsJob> _logger;

    /// <summary>Invitations older than this are permanently deleted.</summary>
    private const int DeleteAfterDays = 30;

    public CleanupExpiredInvitationsJob(HouseFlowDbContext context, ILogger<CleanupExpiredInvitationsJob> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task ExecuteAsync()
    {
        var now = DateTime.UtcNow;

        // 1. Mark pending invitations that have passed their expiry date
        var expiredCount = await _context.Invitations
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatus.Expired));

        if (expiredCount > 0)
            _logger.LogInformation("Marked {Count} expired invitations", expiredCount);

        // 2. Permanently delete old non-pending invitations (accepted/expired/revoked > 30 days)
        var cutoff = now.AddDays(-DeleteAfterDays);
        var deletedCount = await _context.Invitations
            .Where(i => i.Status != InvitationStatus.Pending && i.ExpiresAt <= cutoff)
            .ExecuteDeleteAsync();

        if (deletedCount > 0)
            _logger.LogInformation("Deleted {Count} old invitations (older than {Days} days)", deletedCount, DeleteAfterDays);
    }
}
