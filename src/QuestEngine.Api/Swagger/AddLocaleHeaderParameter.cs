using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace QuestEngine.Api.Swagger;

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

        // If this operation has a "params" query param, add an example showing new format
        var paramsParam = operation.Parameters.FirstOrDefault(p => p.Name == "params" && p.In == ParameterLocation.Query);
        if (paramsParam != null)
        {
            paramsParam.Description ??= "Parameter counts in format: key:count;other:count";
            // ensure schema exists
            paramsParam.Schema ??= new OpenApiSchema { Type = "string" };
            // add example
            paramsParam.Schema.Example = new OpenApiString("golden_wheel_fragments:2;extra_tokens:3");
        }
    }
}
