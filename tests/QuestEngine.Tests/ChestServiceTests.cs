using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using QuestEngine.Application;
using QuestEngine.Domain;
using QuestEngine.Infrastructure;
using Xunit;

public class ChestServiceTests
{
    [Fact]
    public async Task Same_Seed_Gives_Deterministic_Result()
    {
        var opts = new DbContextOptionsBuilder<QuestDbContext>()
            .UseInMemoryDatabase("tests-db").Options;
        using var db = new QuestDbContext(opts);
        var store = new EfProgressStore(db);
        var rng = new HmacRngService("secret");
        var exporter = new StubRewardsExporter();
        var svc = new ChestService(store, rng, exporter);

        var state = await store.GetOrStartAsync("u1", "q1");
        var pool = new RewardPool("t", new [] {
            new PoolVariant("a", 10, new []{ new RewardDef("xp", 10) }),
            new PoolVariant("b", 90, new []{ new RewardDef("gems", 5) }),
        });
        var snapshot = new { chest_id = "c1", pool };
        var chestId = await store.SpawnChestAsync(state, "c1", snapshot);

        var r1 = await svc.OpenAsync("u1","q1", chestId, null);
        var r2 = await svc.OpenAsync("u1","q1", chestId, null);

        r1.BonusCombinationId.Should().Be(r2.BonusCombinationId);
        r1.VariantId.Should().Be(r2.VariantId);
    }
}
