using System.Linq;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace QuestEngine.Api.Swagger;

public class AddParamsExample : IParameterFilter
{
    public void Apply(OpenApiParameter parameter, ParameterFilterContext context)
    {
        if (parameter == null) return;
        if (parameter.In != ParameterLocation.Query) return;
        if (!string.Equals(parameter.Name, "params", System.StringComparison.OrdinalIgnoreCase)) return;

        parameter.Description ??= "Parameter counts in format: key:count;other:count (also accepts commas). Example: golden_wheel_fragments:2;extra_tokens:3";
        parameter.Schema ??= new OpenApiSchema { Type = "string" };
        parameter.Schema.Example = new OpenApiString("golden_wheel_fragments:2;extra_tokens:3");
    }
}
