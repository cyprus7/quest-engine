using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestEngine.Application;

namespace QuestEngine.Infrastructure;

public class QuestDbContext : DbContext
{
    public QuestDbContext(DbContextOptions<QuestDbContext> options) : base(options) {}

    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<QuestProgressRow> QuestProgress => Set<QuestProgressRow>();
    public DbSet<UserParamsRow> UserParams => Set<UserParamsRow>();
    public DbSet<ChestInstanceRow> ChestInstances => Set<ChestInstanceRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRow>().HasKey(x => x.UserId);

        modelBuilder.Entity<QuestProgressRow>().HasKey(x => new { x.UserId, x.QuestId, x.StageKey });
        modelBuilder.Entity<QuestProgressRow>().Property(x => x.StateJson);

        modelBuilder.Entity<UserParamsRow>().HasKey(x => x.UserId);
        modelBuilder.Entity<UserParamsRow>().Property(x => x.TagsJson);
        modelBuilder.Entity<UserParamsRow>().Property(x => x.StatsJson);
        modelBuilder.Entity<UserParamsRow>().Property(x => x.InventoryJson);

        modelBuilder.Entity<ChestInstanceRow>().HasKey(x => x.Id);
        base.OnModelCreating(modelBuilder);
    }
}

public class UserRow
{
    public required string UserId { get; set; }
}

public class QuestProgressRow
{
    public required string UserId { get; set; }
    public required string QuestId { get; set; }
    public required string StageKey { get; set; }
    public string? CurrentSceneId { get; set; }
    public required string StateJson { get; set; } // reserved
    public DateTime UpdatedAt { get; set; }
}

public class UserParamsRow
{
    public required string UserId { get; set; }
    public required string TagsJson { get; set; }
    public required string StatsJson { get; set; }
    public required string InventoryJson { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChestInstanceRow
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string QuestId { get; set; }
    public required string ChestId { get; set; }
    public required string Status { get; set; } // closed|opened
    public required string PoolSnapshotJson { get; set; }
    public string? ResultJson { get; set; }
    public string? BonusCombinationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? OpenedAt { get; set; }
}

// EF-backed store
public sealed class EfProgressStore : IProgressStore
{
    private readonly QuestDbContext _db;

    public EfProgressStore(QuestDbContext db) => _db = db;

    public async Task<UserState> GetOrStartAsync(string userId, string questId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
        {
            user = new UserRow { UserId = userId };
            _db.Users.Add(user);
        }

        // pick stage: first if none
        var progress = await _db.QuestProgress.Where(p => p.UserId == userId && p.QuestId == questId).ToListAsync();
        var stageKey = progress.Count == 0 ? "stage1" : progress.OrderBy(p => p.StageKey).Last().StageKey;
        var currentRow = progress.FirstOrDefault(p => p.StageKey == stageKey);
        if (currentRow is null)
        {
            currentRow = new QuestProgressRow {
                UserId = userId, QuestId = questId, StageKey = stageKey,
                CurrentSceneId = null, StateJson = "{}", UpdatedAt = DateTime.UtcNow
            };
            _db.QuestProgress.Add(currentRow);
        }

        var paramsRow = await _db.UserParams.FindAsync(userId);
        if (paramsRow is null)
        {
            paramsRow = new UserParamsRow {
                UserId = userId, TagsJson = "{}", StatsJson = "{}", InventoryJson = "{}", UpdatedAt = DateTime.UtcNow
            };
            _db.UserParams.Add(paramsRow);
        }

        await _db.SaveChangesAsync();

        return new UserState {
            UserId = userId,
            QuestId = questId,
            CurrentStageKey = stageKey,
            CurrentSceneId = currentRow.CurrentSceneId,
            Tags = JsonSerializer.Deserialize<Dictionary<string,int>>(paramsRow.TagsJson) ?? new(),
            Stats = JsonSerializer.Deserialize<Dictionary<string,int>>(paramsRow.StatsJson) ?? new(),
            Inventory = JsonSerializer.Deserialize<Dictionary<string,int>>(paramsRow.InventoryJson) ?? new(),
        };
    }

    public async Task SaveAsync(UserState state)
    {
        var progress = await _db.QuestProgress.FindAsync(state.UserId, state.QuestId, state.CurrentStageKey);
        if (progress is null)
        {
            progress = new QuestProgressRow {
                UserId = state.UserId, QuestId = state.QuestId, StageKey = state.CurrentStageKey,
                CurrentSceneId = state.CurrentSceneId, StateJson = "{}", UpdatedAt = DateTime.UtcNow
            };
            _db.QuestProgress.Add(progress);
        }
        else
        {
            progress.CurrentSceneId = state.CurrentSceneId;
            progress.UpdatedAt = DateTime.UtcNow;
        }

        var p = await _db.UserParams.FindAsync(state.UserId);
        if (p is null)
        {
            p = new UserParamsRow {
                UserId = state.UserId,
                TagsJson = JsonSerializer.Serialize(state.Tags),
                StatsJson = JsonSerializer.Serialize(state.Stats),
                InventoryJson = JsonSerializer.Serialize(state.Inventory),
                UpdatedAt = DateTime.UtcNow
            };
            _db.UserParams.Add(p);
        }
        else
        {
            p.TagsJson = JsonSerializer.Serialize(state.Tags);
            p.StatsJson = JsonSerializer.Serialize(state.Stats);
            p.InventoryJson = JsonSerializer.Serialize(state.Inventory);
            p.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<string> SpawnChestAsync(UserState state, string chestId, object poolSnapshot)
    {
        var id = Guid.NewGuid().ToString("n");
        var row = new ChestInstanceRow {
            Id = id,
            UserId = state.UserId,
            QuestId = state.QuestId,
            ChestId = chestId,
            Status = "closed",
            PoolSnapshotJson = JsonSerializer.Serialize(poolSnapshot),
            CreatedAt = DateTime.UtcNow
        };
        _db.ChestInstances.Add(row);
        await _db.SaveChangesAsync();
        return id;
    }

    public async Task<ChestInstance?> GetChestAsync(string chestInstanceId)
    {
        var row = await _db.ChestInstances.FindAsync(chestInstanceId);
        if (row is null) return null;
        return new ChestInstance {
            Id = row.Id,
            UserId = row.UserId,
            QuestId = row.QuestId,
            ChestId = row.ChestId,
            Status = row.Status,
            PoolSnapshot = JsonSerializer.Deserialize<object>(row.PoolSnapshotJson) ?? new {},
            ResultSnapshot = row.ResultJson is null ? null : JsonSerializer.Deserialize<object>(row.ResultJson)
        };
    }

    public async Task MarkChestOpenedAsync(string chestInstanceId, object resultSnapshot)
    {
        var row = await _db.ChestInstances.FindAsync(chestInstanceId) ?? throw new InvalidOperationException("Chest not found");
        row.Status = "opened";
        row.ResultJson = JsonSerializer.Serialize(resultSnapshot);
        // Extract combination id if present
        try
        {
            var json = JsonSerializer.Serialize(resultSnapshot);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("BonusCombinationId", out var el))
            {
                row.BonusCombinationId = el.GetString();
            }
        } catch {}

        row.OpenedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
