using System.Collections.Generic;
using QuestEngine.Domain;

namespace QuestEngine.Application;

public interface IContentProvider
{
    QuestContent Get(string questId, string? locale = null);
}

public interface IProgressStore
{
    Task<UserState> GetOrStartAsync(string userId, string questId);
    Task SaveAsync(UserState state);

    Task<string> SpawnChestAsync(UserState state, string chestId, object poolSnapshot);
    Task<ChestInstance?> GetChestAsync(string chestInstanceId);
    Task MarkChestOpenedAsync(string chestInstanceId, object resultSnapshot);
}

public interface IRngService
{
    double Next01(ReadOnlySpan<byte> seed);
}

public interface IRewardsExporter
{
    Task ExportAsync(string userId, IEnumerable<RewardApplied> rewards);
}

public interface IIdempotencyService
{
    Task<bool> TryUseAsync(string userId, string key);
}

public interface IEffectResolver
{
    Task<ApplyResult> ApplyAsync(UserState state, IEnumerable<EffectDef> effects);
}

public interface IChestService
{
    Task<ChestOpenResult> OpenAsync(string userId, string questId, string chestInstanceId, string? idemKey);
}

public interface IQuestRuntime
{
    Task<StateResponse> GetStateAsync(string userId, string questId, string? locale = null);
    Task<StateResponse> GetStageAsync(string questId, IDictionary<string,int> parameters, string? locale = null);
    Task<ChoiceResponse> ApplyChoiceAsync(string userId, string questId, ChoiceRequest req, string? locale = null);
}

// DTOs & State
public sealed class UserState
{
    public required string UserId { get; init; }
    public required string QuestId { get; init; }
    public string CurrentStageKey { get; set; } = "";
    public string? CurrentSceneId { get; set; }

    // было: { get; } = new();
    public Dictionary<string, int> Tags { get; init; } = new();
    public Dictionary<string, int> Stats { get; init; } = new();
    public Dictionary<string, int> Inventory { get; init; } = new();

    public QuestContent? Content { get; set; }
}

public sealed class ChestInstance
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string QuestId { get; init; }
    public required string ChestId { get; init; }
    public required string Status { get; set; } // closed|opened
    public required object PoolSnapshot { get; init; }
    public object? ResultSnapshot { get; set; }
}

public record ApplyResult(IReadOnlyList<object> EffectsApplied,
                          IReadOnlyList<RewardApplied> Rewards,
                          IReadOnlyList<string> SpawnedChestIds);

public record RewardApplied(string Type, int Amount, string? GameId = null, double? Denom = null);

public record ChestOpenResult(string ChestInstanceId, string BonusCombinationId, string VariantId,
                              IReadOnlyList<ChestRewardDto> Rewards);

public record ChestRewardDto(string Type, int Amount, string? GameId, double? Denom);

public record StateResponse(object Scene, IReadOnlyList<object> Choices, object? Timer, object ParamsCurrent);

public record ChoiceRequest(string Choice_Id, string? Current_Scene_Id);

public record ChoiceResponse(
    string PreviousSceneId,
    string SelectedChoiceId,
    object ParamsBefore,
    object ParamsAfter,
    object ParamsDelta,
    IReadOnlyList<object> EffectsApplied,
    IReadOnlyList<object> Rewards,
    object Next);
