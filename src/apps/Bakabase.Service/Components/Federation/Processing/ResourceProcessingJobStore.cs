using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Models.Db;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Federation.Processing;

/// <summary>Bounded disk queue. The requester is part of every key; another paired administrator
/// cannot guess a job id to collect its artifacts or replace its result. No ORM memory cache.</summary>
public sealed class ResourceProcessingJobStore(BakabaseDbContext db)
{
    public static string Key(ResourceProcessingRole role, string requesterId, string jobId) =>
        $"{role}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requesterId))).ToLowerInvariant()}:{jobId}";

    public Task<ResourceProcessingJobDbModel?> GetAsync(ResourceProcessingRole role, string requesterId,
        string jobId, CancellationToken ct) => db.ResourceProcessingJobs.AsNoTracking()
        .SingleOrDefaultAsync(j => j.Id == Key(role, requesterId, jobId), ct);

    public Task<ResourceProcessingJobDbModel?> GetByIdAsync(string id, CancellationToken ct) =>
        db.ResourceProcessingJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct);

    public async Task<IReadOnlyList<ResourceProcessingJobDbModel>> GetRetainedWorkersAsync(string requesterId, CancellationToken ct) =>
        await db.ResourceProcessingJobs.AsNoTracking().Where(j => j.Role == "Worker" && j.RequesterId == requesterId && j.InputJson != "{}")
            .OrderBy(j => j.CreatedAtUtc).Take(16).ToArrayAsync(ct);

    public async Task<ResourceProcessingJobDbModel> AbandonMissingAsync(ResourceProcessingRole role,
        string requesterId, string jobId, CancellationToken ct)
    {
        if (role == ResourceProcessingRole.Manager || !Guid.TryParseExact(jobId, "N", out _))
            throw new ResourceProcessingException("InvalidInput", "A valid owner or worker job id is required.");
        var id = Key(role, requesterId, jobId);
        var now = DateTime.UtcNow;
        var empty = "{}";
        // A timed-out request can arrive after its cancellation. Claim the same durable
        // identity atomically; never overwrite a preparation that already won admission.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT OR IGNORE INTO ResourceProcessingJobs
            (Id, JobId, Role, RequesterId, OwnerId, OwnerEpoch, ResourceId, ResourceRevision,
             RuleHash, Kind, State, Phase, InputJson, ProcessedCount, Attempt, PauseRequested, CancelRequested,
             CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES ({id}, {jobId}, {role.ToString()}, {requesterId}, '', '', 0, 0,
                    '', '', 'Cancelled', 'Cancelled', {empty}, 0, 0, 0, 1, {now}, {now}, 1)
            """, ct);
        return await GetByIdAsync(id, ct) ?? throw new ResourceProcessingException("JobMissing", "The cancelled work record disappeared.");
    }

    public async Task<ResourceProcessingJobDbModel> CreateAsync(ResourceProcessingRole role, string requesterId,
        ResourceProcessingInput input, string inputJson, CancellationToken ct)
    {
        ResourceProcessingWire.ValidateInput(input);
        return await CreateCoreAsync(role, requesterId, input, inputJson, false, ct);
    }

    private async Task<ResourceProcessingJobDbModel> CreateCoreAsync(ResourceProcessingRole role, string requesterId,
        ResourceProcessingInput input, string inputJson, bool batch, CancellationToken ct)
    {
        CheckPayload(inputJson);
        // Serialize admission with the insert. Failed/paused owner jobs and completed-but-unacked
        // worker results still retain payloads and therefore still consume the bounded quota.
        await using var admission = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        var id = Key(role, requesterId, input.JobId);
        var existing = await GetAsync(role, requesterId, input.JobId, ct);
        if (existing != null)
        {
            CheckIdentity(existing, input, batch);
            if (admission != null) await admission.CommitAsync(ct);
            return existing;
        }
        if (role != ResourceProcessingRole.Manager)
        {
            var retained = db.ResourceProcessingJobs.AsNoTracking().Where(j => j.Role == role.ToString() && j.RequesterId == requesterId);
            retained = role == ResourceProcessingRole.Owner
                ? retained.Where(j => j.State != "Completed" && (j.State != "Cancelled" || j.Phase != "Cancelled"))
                : retained.Where(j => j.InputJson != "{}");
            if (await retained.CountAsync(ct) >= 16)
                throw new ResourceProcessingException("PendingJobLimit", "This manager has 16 unfinished processing jobs. Resume or cancel them before starting another.");
        }
        var now = DateTime.UtcNow;
        var row = new ResourceProcessingJobDbModel {Id = id, JobId = input.JobId, Role = role.ToString(),
            RequesterId = requesterId, OwnerId = input.OwnerId, OwnerEpoch = input.OwnerEpoch,
            ResourceId = batch ? 0 : input.ResourceId, ResourceRevision = input.ExpectedResourceRevision ?? 0,
            RuleHash = input.RuleHash, Kind = input.Kind.ToString(), State = ResourceProcessingState.Queued.ToString(),
            InputJson = inputJson, CreatedAtUtc = now, UpdatedAtUtc = now, Version = 1};
        db.ResourceProcessingJobs.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException {SqliteErrorCode: 19})
        {
            db.Entry(row).State = EntityState.Detached;
            existing = await GetAsync(role, requesterId, input.JobId, ct);
            if (existing == null) throw;
            CheckIdentity(existing, input, batch);
            if (admission != null) await admission.CommitAsync(ct);
            return existing;
        }
        if (admission != null) await admission.CommitAsync(ct);
        db.Entry(row).State = EntityState.Detached;
        return row;
    }

    public async Task<ResourceProcessingJobDbModel> CreateBatchAsync(string requesterId, string jobId,
        string ownerId, string ownerEpoch, string ruleHash, ResourceProcessingKind kind, string inputJson,
        CancellationToken ct)
    {
        // A batch is only a manager cursor, never passed to owner/worker resource APIs.
        var input = new ResourceProcessingInput(jobId, ownerId, ownerEpoch, 1, null, ruleHash, kind,
            kind == ResourceProcessingKind.PlayableFiles ? new() : null,
            kind == ResourceProcessingKind.Enhancement ? new() {EnhancerId = 1} : null);
        ResourceProcessingWire.ValidateInput(input);
        return await CreateCoreAsync(ResourceProcessingRole.Manager, requesterId, input, inputJson, true, ct);
    }

    public async Task UpdateAsync(ResourceProcessingJobDbModel row, CancellationToken ct)
    {
        CheckPayload(row.InputJson);
        if (row.ResultJson != null) CheckPayload(row.ResultJson);
        if (row.Error?.Length > 4096) row.Error = row.Error[..4096];
        var now = DateTime.UtcNow;
        var changed = await db.ResourceProcessingJobs.Where(j => j.Id == row.Id && j.Version == row.Version)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, row.State)
                .SetProperty(j => j.ResourceId, row.ResourceId)
                .SetProperty(j => j.ResourceRevision, row.ResourceRevision)
                .SetProperty(j => j.InputJson, row.InputJson).SetProperty(j => j.ResultJson, row.ResultJson)
                .SetProperty(j => j.ErrorCode, row.ErrorCode).SetProperty(j => j.Error, row.Error)
                .SetProperty(j => j.ProcessedCount, row.ProcessedCount).SetProperty(j => j.TotalCount, row.TotalCount)
                .SetProperty(j => j.Phase, row.Phase).SetProperty(j => j.Attempt, row.Attempt)
                .SetProperty(j => j.CurrentStepPhase, row.CurrentStepPhase)
                .SetProperty(j => j.CurrentStepProcessedCount, row.CurrentStepProcessedCount)
                .SetProperty(j => j.CurrentStepTotalCount, row.CurrentStepTotalCount)
                .SetProperty(j => j.PauseRequested, row.PauseRequested).SetProperty(j => j.CancelRequested, row.CancelRequested)
                .SetProperty(j => j.UpdatedAtUtc, now).SetProperty(j => j.Version, j => j.Version + 1), ct);
        if (changed != 1) throw new ResourceProcessingException("JobChanged", "The job changed; reload its current state.");
        row.UpdatedAtUtc = now;
        row.Version++;
    }

    public async Task<IReadOnlyList<ResourceProcessingJobView>> ListAsync(ResourceProcessingRole role,
        string? beforeId, int take, CancellationToken ct, bool activeOnly = false)
    {
        if (take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));
        var query = db.ResourceProcessingJobs.AsNoTracking().Where(j => j.Role == role.ToString());
        if (activeOnly) query = query.Where(j => j.State == "Running" || j.State == "Queued" || j.State == "Paused" || j.State == "Failed" || j.State == "Conflict");
        if (beforeId != null)
        {
            var cursor = await query.Where(j => j.Id == beforeId).Select(j => new { j.CreatedAtUtc, j.Id }).SingleOrDefaultAsync(ct);
            if (cursor == null) return [];
            query = query.Where(j => j.CreatedAtUtc < cursor.CreatedAtUtc ||
                j.CreatedAtUtc == cursor.CreatedAtUtc && string.Compare(j.Id, cursor.Id) < 0);
        }
        return await query.OrderByDescending(j => j.CreatedAtUtc).ThenByDescending(j => j.Id).Take(take).Select(j => new ResourceProcessingJobView(
            j.Id, j.JobId, j.Role, j.OwnerId, j.OwnerEpoch, j.ResourceId, j.Kind, j.State, j.Phase,
            j.ProcessedCount, j.TotalCount, j.Attempt, j.PauseRequested, j.CancelRequested, j.ErrorCode, j.Error,
            j.CreatedAtUtc, j.UpdatedAtUtc, j.Version, j.CurrentStepPhase,
            j.CurrentStepProcessedCount, j.CurrentStepTotalCount)).ToArrayAsync(ct);
    }

    /// <summary>Read execution identity without loading the saved resource, credentials, or rule payload.</summary>
    public async Task<string?> WorkerIdAsync(string id, string localId, CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var close = connection.State != System.Data.ConnectionState.Open;
        if (close) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE Role WHEN 'Worker' THEN $local WHEN 'Manager' THEN
                    CASE json_extract(InputJson,'$.Target.Kind')
                      WHEN 0 THEN OwnerId WHEN 1 THEN $local WHEN 2 THEN json_extract(InputJson,'$.Target.DeviceId')
                    END END FROM ResourceProcessingJobs WHERE Id=$id
                """;
            var key = command.CreateParameter(); key.ParameterName = "$id"; key.Value = id; command.Parameters.Add(key);
            var self = command.CreateParameter(); self.ParameterName = "$local"; self.Value = localId; command.Parameters.Add(self);
            return await command.ExecuteScalarAsync(ct) as string;
        }
        finally { if (close) await connection.CloseAsync(); }
    }

    public Task<ResourceProcessingJobDbModel?> NextQueuedAsync(ResourceProcessingRole role, CancellationToken ct) =>
        db.ResourceProcessingJobs.AsNoTracking().Where(j => j.Role == role.ToString() &&
            j.State == "Queued" && !j.PauseRequested && !j.CancelRequested)
            .OrderBy(j => j.CreatedAtUtc).ThenBy(j => j.Id).FirstOrDefaultAsync(ct);

    /// <summary>Called once before scheduling after startup, never while another runner is active.</summary>
    public Task<int> RecoverInterruptedAsync(ResourceProcessingRole role, CancellationToken ct) =>
        db.ResourceProcessingJobs.Where(j => j.Role == role.ToString() && j.State == "Running")
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.State, j => j.CancelRequested ? "Cancelled" :
                j.PauseRequested ? "Paused" : "Queued").SetProperty(j => j.Version, j => j.Version + 1)
                .SetProperty(j => j.UpdatedAtUtc, DateTime.UtcNow), ct);

    public async Task DeleteTerminalAsync(string id, long expectedVersion, CancellationToken ct)
    {
        var removed = await db.ResourceProcessingJobs.Where(j => j.Id == id && j.Version == expectedVersion &&
            (j.State == "Completed" || j.State == "Cancelled" || j.State == "Failed" || j.State == "Conflict"))
            .ExecuteDeleteAsync(ct);
        if (removed != 1) throw new ResourceProcessingException("JobChanged", "Only an unchanged stopped job can be removed.");
    }

    private static void CheckPayload(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) > ResourceProcessingWire.MaxJsonBytes)
            throw new ResourceProcessingException("BudgetExceeded", "The job payload exceeds its storage budget.");
    }
    private static void CheckIdentity(ResourceProcessingJobDbModel row, ResourceProcessingInput input, bool batch)
    {
        if (row.State == "Cancelled")
            throw new ResourceProcessingException("JobCancelled", "This work was cancelled. Start a new run with a new job id.");
        if (row.OwnerId != input.OwnerId || row.OwnerEpoch != input.OwnerEpoch || !batch && row.ResourceId != input.ResourceId ||
            row.RuleHash != input.RuleHash || row.Kind != input.Kind.ToString())
            throw new ResourceProcessingException("JobConflict", "This job id already belongs to a different processing input.");
    }
}
