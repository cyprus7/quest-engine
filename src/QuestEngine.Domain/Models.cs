using System.Text.Json.Serialization;

namespace QuestEngine.Domain;

public record UiMeta([property: JsonPropertyName("title")] string Title,
                     [property: JsonPropertyName("desc")] string Desc);

public record RewardDef(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("amount")] int Amount,
    [property: JsonPropertyName("denom")] double? Denom = null,
    [property: JsonPropertyName("game_id")] string? GameId = null,
    [property: JsonPropertyName("ui")] UiMeta? Ui = null);

public record RewardFixed(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("amount")] int Amount,
    [property: JsonPropertyName("ui")] UiMeta? Ui = null);

public record PoolVariant(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("weight")] int Weight,
    [property: JsonPropertyName("rewards")] IReadOnlyList<RewardDef> Rewards);

public record RewardPool(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("variants")] IReadOnlyList<PoolVariant> Variants);

public record VariantOverride(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("weight")] int Weight);

public record ChestOverrides(
    [property: JsonPropertyName("variants")] IReadOnlyList<VariantOverride> Variants);

public record ChestDef(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("desc")] string Desc,
    [property: JsonPropertyName("art")] string Art,
    [property: JsonPropertyName("use_pool")] string UsePool,
    [property: JsonPropertyName("overrides")] ChestOverrides? Overrides);

public record EntryCard(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("art")] string Art,
    [property: JsonPropertyName("cta")] string Cta);

public record ChoiceDef(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("effects")] IReadOnlyList<EffectDef> Effects,
    [property: JsonPropertyName("next")] string? Next);

public record SceneDef(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("choices")] IReadOnlyList<ChoiceDef> Choices,
    [property: JsonPropertyName("rewards_on_complete")] IReadOnlyList<RewardFixed>? RewardsOnComplete);

public record StageConnect([property: JsonPropertyName("next_stage_key")] string NextStageKey);

public record StageCondition(
    [property: JsonPropertyName("param")] string Param,
    [property: JsonPropertyName("min")] int Min);

public record StageDef(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("conditions")] IReadOnlyList<StageCondition>? Conditions,
    [property: JsonPropertyName("entry_cards")] IReadOnlyList<EntryCard> EntryCards,
    [property: JsonPropertyName("scenes")] IReadOnlyList<SceneDef> Scenes,
    [property: JsonPropertyName("connect")] StageConnect Connect);

public record QuestContent(
    [property: JsonPropertyName("quest_id")] string QuestId,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("locale")] string Locale,
    [property: JsonPropertyName("meta")] Dictionary<string, object>? Meta,
    [property: JsonPropertyName("reward_pools")] Dictionary<string, RewardPool> RewardPools,
    [property: JsonPropertyName("chests")] Dictionary<string, ChestDef> Chests,
    [property: JsonPropertyName("stages")] IReadOnlyList<StageDef> Stages);

// Effects
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EffectTag), typeDiscriminator: "tag")]
[JsonDerivedType(typeof(EffectStat), typeDiscriminator: "stat")]
[JsonDerivedType(typeof(EffectItem), typeDiscriminator: "item")]
[JsonDerivedType(typeof(EffectSpawnChest), typeDiscriminator: "spawn_chest")]
public abstract record EffectDef([property: JsonPropertyName("type")] string Type);

public record EffectTag(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] int Value) : EffectDef("tag");

public record EffectStat(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] int Value) : EffectDef("stat");

public record EffectItem(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("value")] int Value) : EffectDef("item");

public record EffectSpawnChest(
    [property: JsonPropertyName("chest_id")] string ChestId) : EffectDef("spawn_chest");
