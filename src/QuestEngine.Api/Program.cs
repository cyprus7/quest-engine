using System.Collections.Generic;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.EntityFrameworkCore;
using QuestEngine.Application;
using QuestEngine.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // optional: basic doc info
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "QuestEngine API", Version = "v1" });

    // API key in header
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Auth-Token",
        Description = "API key required to access the endpoints. Enter the token value here."
    });

    // require the key globally
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            new List<string>()
        }
    });

    // add header param for content locale (shows in Swagger UI)
    c.OperationFilter<AddLocaleHeaderParameter>();
});

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

IResult? ValidateApiKey(HttpContext ctx)
{
    var provided = ctx.Request.Headers["X-Auth-Token"].ToString();
    var expected = Environment.GetEnvironmentVariable("QUEST_ENGINE_API_KEY");
    if (provided != expected)
        return Results.StatusCode(403);
    return null;
}

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

app.MapGet("/v1/quests/{questId}/stages", async ([FromRoute] string questId, [FromQuery(Name="params")] string? raw, HttpContext ctx, IQuestRuntime runtime) =>
{
    var auth = ValidateApiKey(ctx);
    if (auth is not null) return auth;

    // default to "ru" if no locale header provided
    var locale = string.IsNullOrWhiteSpace(ctx.Request.Headers["X-Content-Locale"].ToString())
        ? "ru"
        : ctx.Request.Headers["X-Content-Locale"].ToString();

    var prms = ParseParams(raw);
    try
    {
        var res = await runtime.GetStageAsync(questId, prms, locale);
        return Results.Ok(res);
    }
    catch (FileNotFoundException)
    {
        // non-existing quest -> return 4xx indicating invalid parameters
        return Results.BadRequest(new { error = "Invalid quest id" });
    }
    catch (InvalidDataException ex)
    {
        // malformed content / bad snapshot etc. -> treat as client-provided invalid params
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception)
    {
        // unexpected errors still map to 500
        return Results.StatusCode(500);
    }
});

app.MapPost("/v1/quests/{questId}/choice", async ([FromRoute] string questId, [FromBody] ChoiceRequest req, HttpContext ctx, IQuestRuntime runtime) =>
{
    var auth = ValidateApiKey(ctx);
    if (auth is not null) return auth;

    var userId = GetUserId(ctx);
    // default to "ru" if no locale header provided
    var locale = string.IsNullOrWhiteSpace(ctx.Request.Headers["X-Content-Locale"].ToString())
        ? "ru"
        : ctx.Request.Headers["X-Content-Locale"].ToString();
    try
    {
        var res = await runtime.ApplyChoiceAsync(userId, questId, req, locale);
        return Results.Ok(res);
    }
    catch (FileNotFoundException)
    {
        return Results.BadRequest(new { error = "Invalid quest id" });
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception)
    {
        return Results.StatusCode(500);
    }
});

app.MapPost("/v1/quests/{questId}/chests/{chestInstanceId}/open", async ([FromRoute] string questId, [FromRoute] string chestInstanceId, HttpContext ctx, IChestService svc) =>
{
    var auth = ValidateApiKey(ctx);
    if (auth is not null) return auth;

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

// add operation filter implementation for swagger header parameter
public class AddLocaleHeaderParameter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        operation.Parameters ??= new List<OpenApiParameter>();
        if (!operation.Parameters.Any(p => p.Name == "X-Content-Locale"))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-Content-Locale",
                In = ParameterLocation.Header,
                Required = false,
                Description = "Content locale (e.g. 'ru' or 'en'). If omitted, defaults to 'ru'.",
                Schema = new OpenApiSchema { Type = "string", Default = new OpenApiString("ru") }
            });
        }
    }
}
