using Hangfire;
using System.Linq.Expressions;
using System.Reflection;
using HouseFlow.Application.Common;
using HouseFlow.Core.Entities.Common;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HouseFlow.Infrastructure.Jobs;

/// <summary>
/// RGPD Art. 5(1)(e) (limitation de la conservation) et Art. 5(1)(c) (minimisation) —
/// tâche récurrente Hangfire qui applique, en base, les durées de conservation déclarées
/// dans <see cref="DataRetentionOptions"/> (et donc dans la politique de confidentialité
/// et le registre des traitements). Une durée annoncée mais non appliquée est un
/// manquement : c'est ce job qui rend la politique vérifiable.
///
/// Propriétés du job :
/// <list type="bullet">
///   <item><b>Idempotent</b> : deux exécutions successives produisent le même état ; une
///   règle déjà appliquée n'affecte plus aucune ligne.</item>
///   <item><b>Résilient</b> : chaque règle s'exécute dans son propre try/catch ; l'échec
///   d'une règle n'empêche pas les suivantes (et l'exécution suivante rattrapera).</item>
///   <item><b>Par lots</b> : toutes les écritures se font par paquets de
///   <see cref="DataRetentionOptions.BatchSize"/> lignes pour éviter les verrous longs.</item>
///   <item><b>Sans effet de bord sur l'audit</b> : toutes les écritures passent par
///   <c>ExecuteUpdateAsync</c>/<c>ExecuteDeleteAsync</c>, qui court-circuitent le change
///   tracker. Passer par <c>SaveChanges</c> ferait générer un journal d'audit par ligne
///   purgée (<see cref="HouseFlowDbContext"/>) — la purge créerait alors plus de données
///   personnelles qu'elle n'en supprime.</item>
/// </list>
/// </summary>
public class DataRetentionJob
{
    private readonly HouseFlowDbContext _context;
    private readonly DataRetentionOptions _options;
    private readonly ILogger<DataRetentionJob> _logger;
    private readonly TimeProvider _timeProvider;

    public DataRetentionJob(
        HouseFlowDbContext context,
        IOptions<DataRetentionOptions> options,
        ILogger<DataRetentionJob> logger,
        TimeProvider? timeProvider = null)
    {
        _context = context;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // Plusieurs réplicas peuvent héberger le serveur Hangfire : une seule passe à la fois.
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var rules = BuildRules(startedAt).ToList();
        var total = 0;
        var failures = 0;

        foreach (var (rule, action) in rules)
        {
            try
            {
                var affected = await action(cancellationToken);
                total += affected;

                if (affected > 0)
                    _logger.LogInformation("Data retention: {Rule} — {Count} rows affected", rule, affected);
                else
                    _logger.LogDebug("Data retention: {Rule} — {Count} rows affected", rule, affected);
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogError(ex, "Data retention: {Rule} failed — the remaining rules still run", rule);
            }
        }

        _logger.LogInformation(
            "Data retention completed: {Count} rows affected across {RuleCount} rules ({FailureCount} failed) in {ElapsedMs} ms",
            total, rules.Count, failures,
            (long)(_timeProvider.GetUtcNow().UtcDateTime - startedAt).TotalMilliseconds);
    }

    private IEnumerable<(string Rule, Func<CancellationToken, Task<int>> Action)> BuildRules(DateTime now)
    {
        var ipCutoff = now.AddDays(-_options.IpAnonymizeAfterDays);

        // 1. Minimisation des adresses IP au-delà de la fenêtre d'investigation (Art. 5(1)(c)).
        yield return ("anonymize audit log IPs",
            ct => AnonymizeIpColumnAsync(
                _context.AuditLogs.Where(a => a.Timestamp < ipCutoff && a.IpAddress != null),
                a => a.Id, a => a.IpAddress, (s, v) => s.SetProperty(a => a.IpAddress, v), ct));

        yield return ("anonymize refresh token creation IPs",
            ct => AnonymizeIpColumnAsync(
                _context.RefreshTokens.Where(t => t.CreatedAt < ipCutoff && t.CreatedByIp != null),
                t => t.Id, t => t.CreatedByIp, (s, v) => s.SetProperty(t => t.CreatedByIp, v), ct));

        yield return ("anonymize refresh token revocation IPs",
            ct => AnonymizeIpColumnAsync(
                _context.RefreshTokens.Where(t => t.RevokedAt != null && t.RevokedAt < ipCutoff && t.RevokedByIp != null),
                t => t.Id, t => t.RevokedByIp, (s, v) => s.SetProperty(t => t.RevokedByIp, v), ct));

        yield return ("anonymize API key creation IPs",
            ct => AnonymizeIpColumnAsync(
                _context.ApiKeys.Where(k => k.CreatedAt < ipCutoff && k.CreatedByIp != null),
                k => k.Id, k => k.CreatedByIp, (s, v) => s.SetProperty(k => k.CreatedByIp, v), ct));

        // 2. Anonymisation complète des journaux d'audit (considérant 26 : l'enregistrement
        //    sort du champ du RGPD une fois le lien avec la personne rompu).
        yield return ("anonymize audit logs", ct => AnonymizeAuditLogsAsync(now, ct));

        // 3. Purge définitive des journaux d'audit.
        yield return ("delete audit logs", ct => DeleteAuditLogsAsync(now, ct));

        // 4. Refresh tokens révoqués ou expirés (Art. 5(1)(e) + Art. 32).
        yield return ("delete revoked or expired refresh tokens", ct => DeleteRefreshTokensAsync(now, ct));

        // 5. Clés API révoquées.
        yield return ("delete revoked API keys", ct => DeleteApiKeysAsync(now, ct));

        // 6. Entités soft-deleted (un soft delete éternel n'est qu'une pseudonymisation).
        yield return ("purge soft-deleted entities", ct => PurgeSoftDeletedAsync(now, ct));

        // 7. Invitations : marquage des expirées puis suppression des anciennes.
        yield return ("expire and delete invitations", ct => CleanupInvitationsAsync(now, ct));
    }

    // ---------------------------------------------------------------- IP anonymization

    /// <summary>
    /// Tronque une colonne d'adresse IP par lots. <c>ExecuteUpdateAsync</c> ne peut pas
    /// appeler <see cref="IpAddressAnonymizer.Anonymize"/> côté SQL : on lit donc les
    /// couples (clé, IP) par lots — avec un curseur sur la clé primaire, pour ne pas
    /// boucler indéfiniment sur des lignes déjà anonymisées, qui restent sélectionnées par
    /// le filtre — on calcule la valeur tronquée en mémoire, puis on regroupe les mises à
    /// jour par valeur cible (une requête UPDATE par valeur distincte).
    /// </summary>
    private async Task<int> AnonymizeIpColumnAsync<TEntity>(
        IQueryable<TEntity> source,
        Func<TEntity, Guid> keySelector,
        Func<TEntity, string?> ipSelector,
        Action<UpdateSettersBuilder<TEntity>, string?> setIp,
        CancellationToken ct)
        where TEntity : class
    {
        var set = _context.Set<TEntity>();
        var updated = 0;
        var cursor = Guid.Empty;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await source
                .AsNoTracking()
                .Where(e => EF.Property<Guid>(e, "Id") > cursor)
                .OrderBy(e => EF.Property<Guid>(e, "Id"))
                .Take(_options.BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            cursor = keySelector(batch[^1]);

            var groups = batch
                .Where(e => !IpAddressAnonymizer.IsAnonymized(ipSelector(e)))
                .GroupBy(e => IpAddressAnonymizer.Anonymize(ipSelector(e)));

            foreach (var group in groups)
            {
                var ids = group.Select(keySelector).ToList();
                var value = group.Key;
                updated += await set
                    .Where(e => ids.Contains(EF.Property<Guid>(e, "Id")))
                    .ExecuteUpdateAsync(s => setIp(s, value), ct);
            }
        }

        return updated;
    }

    // ------------------------------------------------------------------- Audit logs

    private async Task<int> AnonymizeAuditLogsAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-_options.AuditLogAnonymizeAfterDays);
        var total = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Les lignes traitées ne correspondent plus au prédicat : la boucle se termine.
            var ids = await _context.AuditLogs
                .AsNoTracking()
                .Where(a => a.Timestamp < cutoff &&
                            (a.Username != null || a.UserId != null || a.IpAddress != null ||
                             a.UserAgent != null || a.OldValues != null || a.NewValues != null ||
                             a.ChangedProperties != null))
                .OrderBy(a => a.Id)
                .Select(a => a.Id)
                .Take(_options.BatchSize)
                .ToListAsync(ct);

            if (ids.Count == 0)
                break;

            // EntityType / EntityId / Action / Timestamp sont conservés : statistiques de
            // sécurité sur des enregistrements devenus anonymes (considérant 26).
            total += await _context.AuditLogs
                .Where(a => ids.Contains(a.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.UserId, (Guid?)null)
                    .SetProperty(a => a.Username, (string?)null)
                    .SetProperty(a => a.IpAddress, (string?)null)
                    .SetProperty(a => a.UserAgent, (string?)null)
                    .SetProperty(a => a.OldValues, (string?)null)
                    .SetProperty(a => a.NewValues, (string?)null)
                    .SetProperty(a => a.ChangedProperties, (string?)null)
                    .SetProperty(a => a.AdditionalData, (string?)null), ct);
        }

        return total;
    }

    private Task<int> DeleteAuditLogsAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-_options.AuditLogDeleteAfterDays);
        return DeleteInBatchesAsync(
            _context.AuditLogs.Where(a => a.Timestamp < cutoff), a => a.Id, ct);
    }

    // -------------------------------------------------------------- Tokens & API keys

    private Task<int> DeleteRefreshTokensAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-_options.RevokedRefreshTokenRetentionDays);
        return DeleteInBatchesAsync(
            _context.RefreshTokens.Where(t => (t.RevokedAt != null && t.RevokedAt < cutoff) || t.ExpiresAt < cutoff),
            t => t.Id, ct);
    }

    private Task<int> DeleteApiKeysAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-_options.RevokedApiKeyRetentionDays);
        return DeleteInBatchesAsync(
            _context.ApiKeys.Where(k => k.RevokedAt != null && k.RevokedAt < cutoff), k => k.Id, ct);
    }

    // ------------------------------------------------------------------ Soft deletes

    private static readonly MethodInfo PurgeSoftDeletedForTypeMethod =
        typeof(DataRetentionJob).GetMethod(nameof(PurgeSoftDeletedForTypeAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// Purge générique, pilotée par le modèle EF : toute entité implémentant
    /// <see cref="ISoftDeletable"/> est concernée, y compris celles ajoutées plus tard
    /// (aucune n'existe à ce jour, mais l'infrastructure de soft delete, elle, existe).
    /// <c>IgnoreQueryFilters</c> est indispensable : le filtre global masque justement les
    /// lignes à purger.
    /// </summary>
    private async Task<int> PurgeSoftDeletedAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-_options.SoftDeletedRetentionDays);
        var total = 0;

        var softDeletableTypes = _context.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && typeof(ISoftDeletable).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType)
            .Distinct();

        foreach (var clrType in softDeletableTypes)
        {
            var task = (Task<int>)PurgeSoftDeletedForTypeMethod
                .MakeGenericMethod(clrType)
                .Invoke(this, [cutoff, ct])!;
            total += await task;
        }

        return total;
    }

    private Task<int> PurgeSoftDeletedForTypeAsync<TEntity>(DateTime cutoff, CancellationToken ct)
        where TEntity : class, ISoftDeletable
        => DeleteInBatchesAsync(
            _context.Set<TEntity>().IgnoreQueryFilters()
                .Where(e => e.IsDeleted && e.DeletedAt != null && e.DeletedAt < cutoff),
            e => EF.Property<Guid>(e, "Id"), ct);

    // ------------------------------------------------------------------- Invitations

    private async Task<int> CleanupInvitationsAsync(DateTime now, CancellationToken ct)
    {
        // 1. Marque comme expirées les invitations en attente dont la date est dépassée.
        var expired = await _context.Invitations
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatus.Expired), ct);

        // 2. Supprime les invitations non-pending (acceptées / expirées / révoquées) anciennes.
        var cutoff = now.AddDays(-_options.ExpiredInvitationRetentionDays);
        var deleted = await DeleteInBatchesAsync(
            _context.Invitations.Where(i => i.Status != InvitationStatus.Pending && i.ExpiresAt <= cutoff),
            i => i.Id, ct);

        return expired + deleted;
    }

    // ----------------------------------------------------------------------- Helpers

    /// <summary>
    /// Supprime par lots les lignes correspondant à <paramref name="source"/>. On lit
    /// d'abord les clés puis on supprime par clé, plutôt que d'utiliser <c>Take</c> dans un
    /// <c>ExecuteDelete</c> : tous les fournisseurs ne traduisent pas une limite dans un
    /// DELETE. Les lignes supprimées ne correspondent plus au filtre : la boucle se termine
    /// et le job reste rejouable.
    /// </summary>
    private async Task<int> DeleteInBatchesAsync<TEntity>(
        IQueryable<TEntity> source,
        Expression<Func<TEntity, Guid>> keySelector,
        CancellationToken ct)
        where TEntity : class
    {
        var total = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var ids = await source.AsNoTracking()
                .OrderBy(keySelector)
                .Select(keySelector)
                .Take(_options.BatchSize)
                .ToListAsync(ct);

            if (ids.Count == 0)
                break;

            total += await source.Where(BuildIdFilter(keySelector, ids)).ExecuteDeleteAsync(ct);
        }

        return total;
    }

    private static Expression<Func<TEntity, bool>> BuildIdFilter<TEntity>(
        Expression<Func<TEntity, Guid>> keySelector,
        List<Guid> ids)
    {
        var contains = Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), [typeof(Guid)],
            Expression.Constant(ids), keySelector.Body);

        return Expression.Lambda<Func<TEntity, bool>>(contains, keySelector.Parameters);
    }
}
