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

app.MapGet("/v1/quests/{questId}/state", async ([FromRoute] string questId, HttpContext ctx, IQuestRuntime runtime) =>
{
    var userId = GetUserId(ctx);
    var res = await runtime.GetStateAsync(userId, questId);
    return Results.Ok(res);
});

app.MapPost("/v1/quests/{questId}/choice", async ([FromRoute] string questId, [FromBody] ChoiceRequest req, HttpContext ctx, IQuestRuntime runtime) =>
{
    var userId = GetUserId(ctx);
    var res = await runtime.ApplyChoiceAsync(userId, questId, req);
    return Results.Ok(res);
});

app.MapPost("/v1/chests/{chestInstanceId}/open", async ([FromRoute] string chestInstanceId, HttpContext ctx, IChestService svc) =>
{
    var userId = GetUserId(ctx);
    var questId = ctx.Request.Query["questId"].ToString();
    var idem = ctx.Request.Headers["Idempotency-Key"].ToString();
    var res = await svc.OpenAsync(userId, questId, chestInstanceId, string.IsNullOrEmpty(idem) ? null : idem);
    return Results.Ok(res);
});

app.Run();
