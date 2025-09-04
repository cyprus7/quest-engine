using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using QuestEngine.Domain;

namespace QuestEngine.Application;

public sealed class FileContentProvider : IContentProvider
{
    private readonly string _folder;
    private readonly Dictionary<string, QuestContent> _cache = new();

    public FileContentProvider(string folder) => _folder = folder;

    public QuestContent Get(string questId, string? locale = null)
    {
        var cacheKey = $"{questId}|{locale}";
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(locale))
        {
            candidates.Add(Path.Combine(_folder, locale, $"{questId}.json"));         // content/<locale>/<questId>.json
            candidates.Add(Path.Combine(_folder, $"{questId}_{locale}.json"));       // content/quest_locale.json
        }
        candidates.Add(Path.Combine(_folder, $"{questId}.json"));                   // content/questId.json

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) throw new FileNotFoundException($"Content {questId} not found (locale={locale})");

        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var doc = JsonSerializer.Deserialize<QuestContent>(json, opts) ?? throw new InvalidDataException("Invalid JSON");
        _cache[cacheKey] = doc;
        return doc;
    }
}

public sealed class HmacRngService : IRngService
{
    private readonly byte[] _secret;

    public HmacRngService(string secret) => _secret = Encoding.UTF8.GetBytes(secret);

    public double Next01(ReadOnlySpan<byte> seed)
    {
        using var h = new HMACSHA256(_secret);
        var b = h.ComputeHash(seed.ToArray());
        // first 8 bytes -> UInt64
        ulong x = BitConverter.ToUInt64(b, 0);
        return (x >> 11) * (1.0 / (1UL << 53));
    }
}

public sealed class StubRewardsExporter : IRewardsExporter
{
    public Task ExportAsync(string userId, IEnumerable<RewardApplied> rewards)
    {
        // Stub: log to console
        Console.WriteLine($"[RewardsExporter] {userId} -> {string.Join(", ", rewards.Select(r => r.Type+":"+r.Amount))}");
        return Task.CompletedTask;
    }
}

public sealed class InMemoryIdemService : IIdempotencyService
{
    private readonly HashSet<string> _keys = new();
    private readonly object _lock = new();

    public Task<bool> TryUseAsync(string userId, string key)
    {
        lock (_lock)
        {
            var composite = $"{userId}:{key}";
            if (_keys.Contains(composite)) return Task.FromResult(false);
            _keys.Add(composite);
            return Task.FromResult(true);
        }
    }
}

public sealed class EffectResolver : IEffectResolver
{
    private readonly IProgressStore _store;

    public EffectResolver(IProgressStore store) => _store = store;

    public async Task<ApplyResult> ApplyAsync(UserState state, IEnumerable<EffectDef> effects)
    {
        var applied = new List<object>();
        var spawnedChests = new List<string>();
        var rewards = new List<RewardApplied>();

        foreach (var e in effects)
        {
            switch (e)
            {
                case EffectTag t:
                    state.Tags[t.Key] = state.Tags.GetValueOrDefault(t.Key) + t.Value;
                    applied.Add(new { type = "tag", t.Key, t.Value });
                    break;

                case EffectStat s:
                    state.Stats[s.Key] = state.Stats.GetValueOrDefault(s.Key) + s.Value;
                    applied.Add(new { type = "stat", s.Key, s.Value });
                    break;

                case EffectItem it:
                    state.Inventory[it.Id] = state.Inventory.GetValueOrDefault(it.Id) + it.Value;
                    applied.Add(new { type = "item", it.Id, it.Value });
                    break;

                case EffectSpawnChest sc:
                    var content = state.Content!;
                    var chest = content.Chests[sc.ChestId];
                    var basePool = content.RewardPools[chest.UsePool];
                    var merged = MergePool(basePool, chest.Overrides);
                    var snapshot = new { chest_id = sc.ChestId, pool = merged };
                    var instId = await _store.SpawnChestAsync(state, sc.ChestId, snapshot);
                    spawnedChests.Add(instId);
                    applied.Add(new { type = "spawn_chest", chest_id = sc.ChestId, chest_instance_id = instId });
                    break;
            }
        }

        await _store.SaveAsync(state);
        return new ApplyResult(applied, rewards, spawnedChests);
    }

    private static RewardPool MergePool(RewardPool basePool, ChestOverrides? overrides)
    {
        if (overrides is null) return basePool;
        var map = basePool.Variants.ToDictionary(v => v.Id, v => v);
        foreach (var ov in overrides.Variants)
        {
            if (map.TryGetValue(ov.Id, out var v))
            {
                map[ov.Id] = v with { Weight = ov.Weight };
            }
        }
        return basePool with { Variants = map.Values.ToList() };
    }
}

public sealed class ChestService : IChestService
{
    private readonly IProgressStore _store;
    private readonly IRngService _rng;
    private readonly IRewardsExporter _exporter;

    public ChestService(IProgressStore store, IRngService rng, IRewardsExporter exporter)
        => (_store, _rng, _exporter) = (store, rng, exporter);

    public async Task<ChestOpenResult> OpenAsync(string userId, string questId, string chestInstanceId, string? idemKey)
    {
        var chest = await _store.GetChestAsync(chestInstanceId) ?? throw new InvalidOperationException("Chest not found");
        if (chest.QuestId != questId)
            throw new InvalidOperationException("Chest not in this quest");
        if (chest.Status == "opened" && chest.ResultSnapshot is ChestOpenResult prev) return prev;

        // restore pool
        var poolJson = JsonSerializer.Serialize(chest.PoolSnapshot);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var snap = JsonSerializer.Deserialize<PoolSnapshot>(poolJson, opts) ?? throw new InvalidDataException("Bad snapshot");
        var pool = snap.pool;

        Span<byte> seed = stackalloc byte[128];
        var seedStr = $"{userId}|{questId}|{chestInstanceId}";
        Encoding.UTF8.GetBytes(seedStr, seed);
        var u = _rng.Next01(seed);
        var weights = pool.Variants.Select(v => v.Weight).ToList();
        var idx = WeightedPicker.PickIndex(weights, u);
        var variant = pool.Variants[idx];

        var comboId = BonusCombinationId.Compute(variant.Rewards);
        var applied = variant.Rewards.Select(r => new RewardApplied(r.Type, r.Amount, r.GameId, r.Denom)).ToList();

        await _exporter.ExportAsync(userId, applied);

        var result = new ChestOpenResult(chestInstanceId, comboId, variant.Id,
            applied.Select(a => new ChestRewardDto(a.Type, a.Amount, a.GameId, a.Denom)).ToList());

        await _store.MarkChestOpenedAsync(chestInstanceId, result);
        return result;
    }

    private record PoolSnapshot(string chest_id, RewardPool pool);
}

public sealed class QuestRuntime : IQuestRuntime
{
    private readonly IContentProvider _content;
    private readonly IProgressStore _store;
    private readonly IEffectResolver _effects;

    public QuestRuntime(IContentProvider content, IProgressStore store, IEffectResolver effects)
        => (_content, _store, _effects) = (content, store, effects);

    public async Task<StateResponse> GetStateAsync(string userId, string questId, string? locale = null)
    {
        var content = _content.Get(questId, locale);
        var s = await _store.GetOrStartAsync(userId, questId);
        s.Content = content;

        var stage = content.Stages.First(st => st.Key == s.CurrentStageKey);
        var scene = stage.Scenes.FirstOrDefault(x => x.Id == s.CurrentSceneId) ?? stage.Scenes.First();

        return new StateResponse(
            Scene: new { id = scene.Id, title = stage.Title, description = scene.Text, image = stage.EntryCards.FirstOrDefault()?.Art },
            Choices: scene.Choices.Select(c => (object)new { id = c.Id, text = c.Label }).ToList(),
            Timer: (object?)new { ends_at = DateTimeOffset.UtcNow.AddMinutes(30), duration_seconds = 1800 },
            ParamsCurrent: Snapshot(s)
        );
    }

    public Task<StateResponse> GetStageAsync(string questId, IDictionary<string,int> parameters, string? locale = null)
    {
        var content = _content.Get(questId, locale);
        var stage = content.Stages.First();
        foreach (var st in content.Stages)
        {

            var satisfies = st.Conditions == null || st.Conditions.All(c =>
            {
                var has = parameters.TryGetValue(c.Param, out var v);
                return (has ? v : 0) >= c.Min;
            });

            if (satisfies)
                stage = st;
            else
                break;
        }

        var scene = stage.Scenes.First();

        return Task.FromResult(new StateResponse(
            Scene: new { id = scene.Id, stage_key = stage.Key, title = stage.Title, description = scene.Text, image = stage.EntryCards.FirstOrDefault()?.Art },
            Choices: scene.Choices.Select(c => (object)new { id = c.Id, text = c.Label }).ToList(),
            Timer: null,
            ParamsCurrent: new { inventory = parameters }
        ));
    }

    public async Task<ChoiceResponse> ApplyChoiceAsync(string userId, string questId, ChoiceRequest req, string? locale = null)
    {
        var content = _content.Get(questId, locale);
        var s = await _store.GetOrStartAsync(userId, questId);
        s.Content = content;

        var stage = content.Stages.First(st => st.Key == s.CurrentStageKey);
        // prefer client-provided current scene id if present, else use stored state, else first scene
        SceneDef scene;
        if (!string.IsNullOrEmpty(req.CurrentSceneId))
        {
            scene = stage.Scenes.FirstOrDefault(x => x.Id == req.CurrentSceneId)
                ?? throw new InvalidOperationException("Unknown scene");
        }
        else
        {
            scene = stage.Scenes.FirstOrDefault(x => x.Id == s.CurrentSceneId) ?? stage.Scenes.First();
        }
        var choice = scene.Choices.FirstOrDefault(c => c.Id == req.ChoiceId)
             ?? throw new InvalidOperationException("Unknown choice");

        var before = Snapshot(s);
        var applied = await _effects.ApplyAsync(s, choice.Effects);

        if (string.IsNullOrEmpty(choice.Next))
        {
            // stage complete
            var fixedRewards = scene.RewardsOnComplete ?? Array.Empty<RewardFixed>();
            var rewards = new List<object>();
            foreach (var r in fixedRewards)
            {
                s.Inventory[r.Id] = s.Inventory.GetValueOrDefault(r.Id) + r.Amount;
                rewards.Add(new { id = r.Id, type = r.Type, value = r.Amount, source = "scene" });
            }
            // advance stage
            var nextKey = stage.Connect.NextStageKey;
            s.CurrentStageKey = nextKey;
            s.CurrentSceneId = null;
            await _store.SaveAsync(s);
        }
        else
        {
            s.CurrentSceneId = choice.Next;
            await _store.SaveAsync(s);
        }

        var after = Snapshot(s);

        var delta = new {
            tags = Diff(before.tags, after.tags),
            stats = Diff(before.stats, after.stats),
            inventory = Diff(before.inventory, after.inventory)
        };

        var next = await GetStateAsync(userId, questId, locale);

        return new ChoiceResponse(
            PreviousSceneId: scene.Id,
            SelectedChoiceId: choice.Id,
            ParamsBefore: before,
            ParamsAfter: after,
            ParamsDelta: delta,
            EffectsApplied: applied.EffectsApplied,
            Rewards: applied.Rewards.Select(r => (object)new { type = r.Type, value = r.Amount, r.GameId, r.Denom }).ToList(),
            Next: next
        );
    }

    private static dynamic Snapshot(UserState s) => new {
        tags = s.Tags.ToDictionary(k => k.Key, v => v.Value),
        stats = s.Stats.ToDictionary(k => k.Key, v => v.Value),
        inventory = s.Inventory.ToDictionary(k => k.Key, v => v.Value)
    };

    private static Dictionary<string,int> Diff(Dictionary<string,int> a, Dictionary<string,int> b)
    {
        var keys = a.Keys.Union(b.Keys).ToHashSet();
        var d = new Dictionary<string,int>();
        foreach (var k in keys)
        {
            var av = a.GetValueOrDefault(k);
            var bv = b.GetValueOrDefault(k);
            var dv = bv - av;
            if (dv != 0) d[k] = dv;
        }
        return d;
    }
}
