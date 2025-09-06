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
    // small fake content provider used by tests
    private class FakeContentProvider : IContentProvider
    {
        private readonly QuestContent _content;
        public FakeContentProvider(QuestContent content) => _content = content;
        public QuestContent Get(string questId, string? locale = null)
        {
            if (_content.QuestId != questId) throw new FileNotFoundException();
            return _content;
        }
    }

    [Fact]
    public async Task Same_Seed_Gives_Deterministic_Result()
    {
        var rng = new HmacRngService("secret");
        var exporter = new StubRewardsExporter();

        // create content with a pool and chest "c1"
        var pool = new RewardPool("t", new [] {
            new PoolVariant("a", 10, new []{ new RewardDef("xp", 10) }),
            new PoolVariant("b", 90, new []{ new RewardDef("gems", 5) }),
        });
        var content = new QuestContent("q1","1.0","ru", null,
            new Dictionary<string, RewardPool> { { "pool.training.fs", pool } },
            new Dictionary<string, ChestDef> {
                { "c1", new ChestDef("t","d","art","pool.training.fs", null) }
            },
            new List<StageDef>()
        );

        var provider = new FakeContentProvider(content);
        var svc = new ChestService(provider, rng, exporter);

        var r1 = await svc.OpenAsync("u1","q1", "c1", null);
        var r2 = await svc.OpenAsync("u1","q1", "c1", null);

        r1.BonusCombinationId.Should().Be(r2.BonusCombinationId);
        r1.VariantId.Should().Be(r2.VariantId);
    }

    [Fact]
    public async Task Cannot_Open_Chest_From_Another_Quest()
    {
        var rng = new HmacRngService("secret");
        var exporter = new StubRewardsExporter();

        var pool = new RewardPool("t", new [] {
            new PoolVariant("a", 10, new []{ new RewardDef("xp", 10) })
        });
        var content = new QuestContent("q1","1.0","ru", null,
            new Dictionary<string, RewardPool> { { "pool.training.fs", pool } },
            new Dictionary<string, ChestDef> {
                { "c1", new ChestDef("t","d","art","pool.training.fs", null) }
            },
            new List<StageDef>()
        );

        var provider = new FakeContentProvider(content);
        var svc = new ChestService(provider, rng, exporter);

        // opening c1 under q2 should fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.OpenAsync("u1", "q2", "c1", null));
    }
}
