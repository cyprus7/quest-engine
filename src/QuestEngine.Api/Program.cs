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

// global error-logging middleware: logs any non-success responses (>=400) and unhandled exceptions
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
        var status = ctx.Response.StatusCode;
        if (status >= 400)
        {
            var user = ctx.Request.Headers.TryGetValue("X-User-Id", out var u) ? u.ToString() : "-";
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
            Console.Error.WriteLine($"[ERROR] {DateTime.UtcNow:o} {ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString} => {status} user={user} ip={ip}");
        }
    }
    catch (Exception ex)
    {
        var user = ctx.Request.Headers.TryGetValue("X-User-Id", out var u) ? u.ToString() : "-";
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
        Console.Error.WriteLine($"[ERROR] {DateTime.UtcNow:o} Exception processing {ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString} user={user} ip={ip} ex={ex}");
        throw;
    }
});

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

// new helper: resolve locale from query header or Accept-Language (fallback "ru")
string ResolveLocale(HttpContext ctx, string? queryLocale = null)
{
    if (!string.IsNullOrWhiteSpace(queryLocale)) return queryLocale!;
    var header = ctx.Request.Headers["X-Content-Locale"].ToString();
    if (!string.IsNullOrWhiteSpace(header)) return header;
    var al = ctx.Request.Headers["Accept-Language"].ToString();
    if (!string.IsNullOrWhiteSpace(al))
    {
        // Accept-Language may be like "en-US,en;q=0.9,ru;q=0.8" -> take first token and two-letter code
        var first = al.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (!string.IsNullOrWhiteSpace(first))
        {
            var code = first.Split(';', 2)[0].Split('-', 2)[0];
            if (!string.IsNullOrWhiteSpace(code)) return code;
        }
    }
    return "ru";
}

// new: simple console logger enabled when LOG_LEVEL=info (also logs Accept-Language and resolved locale)
void LogRequest(HttpContext ctx, string route, object? payload = null, string? queryLocale = null)
{
    var lvl = Environment.GetEnvironmentVariable("LOG_LEVEL");
    if (!string.Equals(lvl, "info", StringComparison.OrdinalIgnoreCase)) return;

    var user = ctx.Request.Headers.TryGetValue("X-User-Id", out var v) ? v.ToString() : "-";
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "-";
    var authPresent = !string.IsNullOrEmpty(ctx.Request.Headers["X-Auth-Token"].ToString());
    var query = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : "";
    var acceptLang = ctx.Request.Headers["Accept-Language"].ToString();
    var resolved = ResolveLocale(ctx, queryLocale);

    Console.WriteLine($"[INFO] {DateTime.UtcNow:o} {ctx.Request.Method} {route} user={user} ip={ip} auth={(authPresent ? "yes":"no")} query={query} accept-language=\"{acceptLang}\" resolved-locale=\"{resolved}\"");
    if (payload is not null)
    {
        try { Console.WriteLine($"[INFO] Payload: {JsonSerializer.Serialize(payload)}"); }
        catch { /* ignore serialization errors */ }
    }
}

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

app.MapGet("/v1/quests/{questId}/stages", async ([FromRoute] string questId,
                                                 [FromQuery(Name="params")] string? raw,
                                                 [FromQuery(Name="sceneId")] string? sceneId,
                                                 [FromQuery(Name="all")] bool? all,
+                                                [FromQuery(Name="locale")] string? qLocale,
                                                 HttpContext ctx,
                                                 IQuestRuntime runtime,
                                                 IContentProvider contentProvider) =>
{
-    LogRequest(ctx, $"/v1/quests/{questId}/stages", new { sceneId, raw, all });
+    LogRequest(ctx, $"/v1/quests/{questId}/stages", new { sceneId, raw, all }, qLocale);
    var auth = ValidateApiKey(ctx);
    if (auth is not null) return auth;

-    var locale = string.IsNullOrWhiteSpace(ctx.Request.Headers["X-Content-Locale"].ToString())
-        ? "ru"
-        : ctx.Request.Headers["X-Content-Locale"].ToString();
+    var locale = ResolveLocale(ctx, qLocale);

    var prms = ParseParams(raw);
    try
    {
        // if caller requested all stages, return all with status locked|available
        if (all is true)
        {
            var content = contentProvider.Get(questId, locale);
            // determine last index of stages that satisfy parameters (same logic as runtime.GetStageAsync)
            int lastAvailable = -1;
            for (int i = 0; i < content.Stages.Count; i++)
            {
                var st = content.Stages[i];
                var satisfies = st.Conditions == null || st.Conditions.All(c =>
                {
                    var has = prms.TryGetValue(c.Param, out var v);
                    return (has ? v : 0) >= c.Min;
                });
                if (satisfies)
                    lastAvailable = i;
                else
                    break;
            }

            var stages = content.Stages.Select((st, idx) => new
            {
                key = st.Key,
                title = st.Title,
                status = idx <= lastAvailable ? "available" : "locked",
                entry_cards = st.EntryCards,
                scenes = st.Scenes
            }).ToList();

            return Results.Ok(new { quest_id = content.QuestId, stages });
        }

        // default: existing single-stage behavior
        StateResponse result;
        if (!string.IsNullOrWhiteSpace(sceneId))
            result = await runtime.GetSceneAsync(questId, sceneId!, locale);
        else
            result = await runtime.GetStageAsync(questId, prms, locale);

        return Results.Ok(result);
    }
    catch (FileNotFoundException)
    {
        // non-existing quest -> return 4xx indicating invalid parameters
        return Results.BadRequest(new { error = "Invalid quest id" });
    }
    catch (InvalidOperationException ex)
    {
        // unknown scene or other invalid operation -> treat as bad request
        return Results.BadRequest(new { error = ex.Message });
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

app.MapPost("/v1/quests/{questId}/choice", async ([FromRoute] string questId, [FromBody] ChoiceRequest req, [FromQuery(Name="locale")] string? qLocale, HttpContext ctx, IQuestRuntime runtime) =>
{
-    LogRequest(ctx, $"/v1/quests/{questId}/choice", req);
+    LogRequest(ctx, $"/v1/quests/{questId}/choice", req, qLocale);
    var auth = ValidateApiKey(ctx);
    if (auth is not null) return auth;

    var userId = GetUserId(ctx);
    // default to "ru" if no locale header provided
-    var locale = string.IsNullOrWhiteSpace(ctx.Request.Headers["X-Content-Locale"].ToString())
-        ? "ru"
-        : ctx.Request.Headers["X-Content-Locale"].ToString();
+    var locale = ResolveLocale(ctx, qLocale);
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

app.MapPost("/v1/quests/{questId}/chests/{chestInstanceId}/open", async ([FromRoute] string questId, [FromRoute] string chestInstanceId, [FromQuery(Name="locale")] string? qLocale, HttpContext ctx, IChestService svc) =>
{
-    var idemHeader = ctx.Request.Headers["Idempotency-Key"].ToString();
-    LogRequest(ctx, $"/v1/quests/{questId}/chests/{chestInstanceId}/open", new { idem = idemHeader });
+    var idemHeader = ctx.Request.Headers["Idempotency-Key"].ToString();
+    LogRequest(ctx, $"/v1/quests/{questId}/chests/{chestInstanceId}/open", new { idem = idemHeader }, qLocale);
    var auth = ValidateApiKey(ctx);
    if (auth is not null) return auth;

    var userId = GetUserId(ctx);
-    var idem = ctx.Request.Headers["Idempotency-Key"].ToString();
+    var idem = ctx.Request.Headers["Idempotency-Key"].ToString();
+    var locale = ResolveLocale(ctx, qLocale);
    try
    {
-        var res = await svc.OpenAsync(userId, questId, chestInstanceId, string.IsNullOrEmpty(idem) ? null : idem);
+        var res = await svc.OpenAsync(userId, questId, chestInstanceId, string.IsNullOrEmpty(idem) ? null : idem);
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
