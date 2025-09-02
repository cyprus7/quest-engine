using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestEngine.Application;
using QuestEngine.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configuration
var contentFolder = Path.Combine(AppContext.BaseDirectory, "content");
Directory.CreateDirectory(contentFolder);

// EF Core - Postgres (or InMemory for quick start)
var conn = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrEmpty(conn))
{
    builder.Services.AddDbContext<QuestDbContext>(opt => opt.UseNpgsql(conn));
}
else
{
    builder.Services.AddDbContext<QuestDbContext>(opt => opt.UseInMemoryDatabase("questdb"));
}

// DI
builder.Services.AddSingleton<IContentProvider>(new FileContentProvider(contentFolder));
builder.Services.AddScoped<IProgressStore, EfProgressStore>();
builder.Services.AddSingleton<IRngService>(new HmacRngService("rotate-this-secret"));
builder.Services.AddSingleton<IRewardsExporter, StubRewardsExporter>();
builder.Services.AddSingleton<IIdempotencyService, InMemoryIdemService>();
builder.Services.AddScoped<IEffectResolver, EffectResolver>();
builder.Services.AddScoped<IChestService, ChestService>();
builder.Services.AddScoped<IQuestRuntime, QuestRuntime>();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));

// seed demo content if missing
app.Lifetime.ApplicationStarted.Register(() => {
    var demoPath = Path.Combine(contentFolder, "mostbet_odyssey_v1.json");
    if (!File.Exists(demoPath))
    {
        var demoJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "content", "mostbet_odyssey_v1.json"));
        File.WriteAllText(demoPath, demoJson);
    }
});

string GetUserId(HttpContext ctx) => ctx.Request.Headers.TryGetValue("X-User-Id", out var v) ? v.ToString() : "demo-user";

static Dictionary<string,int> ParseParams(string? raw)
{
    var dict = new Dictionary<string,int>();
    if (string.IsNullOrWhiteSpace(raw)) return dict;
    var pairs = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
    foreach (var p in pairs)
    {
        var parts = p.Split(';', 2);
        if (parts.Length == 2 && int.TryParse(parts[1], out var val))
            dict[parts[0]] = val;
    }
    return dict;
}

app.MapGet("/v1/quests/{questId}/stages", async ([FromRoute] string questId, [FromQuery(Name="params")] string? raw, IQuestRuntime runtime) =>
{
    var prms = ParseParams(raw);
    var res = await runtime.GetStageAsync(questId, prms);
    return Results.Ok(res);
});

app.MapPost("/v1/quests/{questId}/choice", async ([FromRoute] string questId, [FromBody] ChoiceRequest req, HttpContext ctx, IQuestRuntime runtime) =>
{
    var userId = GetUserId(ctx);
    try
    {
        var res = await runtime.ApplyChoiceAsync(userId, questId, req);
        return Results.Ok(res);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/v1/quests/{questId}/chests/{chestInstanceId}/open", async ([FromRoute] string questId, [FromRoute] string chestInstanceId, HttpContext ctx, IChestService svc) =>
{
    var userId = GetUserId(ctx);
    var idem = ctx.Request.Headers["Idempotency-Key"].ToString();
    try
    {
        var res = await svc.OpenAsync(userId, questId, chestInstanceId, string.IsNullOrEmpty(idem) ? null : idem);
        return Results.Ok(res);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();
