using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;

namespace isg_api_pulso.Swagger
{
    public class SwaggerExamplesOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (operation == null || operation.RequestBody == null) return;

            // Only add example for the PeticionSpDto request body
            foreach (var content in operation.RequestBody.Content)
            {
                if (content.Key != "application/json") continue;

                var schemaRef = content.Value.Schema?.Reference?.Id;
                if (string.Equals(schemaRef, "PeticionSpDto", System.StringComparison.OrdinalIgnoreCase))
                {
                    var example = new OpenApiObject
                    {
                        ["NombreSp"] = new OpenApiString("sp_ISG_Vision_VentasDiarias"),
                        ["Parametros"] = new OpenApiObject
                        {
                            ["DesdeFecha"] = new OpenApiString("2026-07-01"),
                            ["HastaFecha"] = new OpenApiString("2026-07-15")
                        }
                    };

                    content.Value.Example = example;
                }
            }
        }
    }
}
