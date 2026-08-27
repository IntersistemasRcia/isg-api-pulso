using Microsoft.Data.SqlClient;
using System.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Logging;
using System.IdentityModel.Tokens.Jwt;
// Not using Microsoft.OpenApi.Models directly to avoid package version conflicts with Swashbuckle
// Avoid direct OpenApi model types to prevent package version conflicts on net10.0
// Swagger OpenApi types removed to avoid package conflicts; using default AddSwaggerGen()

var builder = WebApplication.CreateBuilder(args);

// Mostrar PII de identidad en Development para facilitar diagnóstico de validación JWT
if (builder.Environment.IsDevelopment())
{
    IdentityModelEventSource.ShowPII = true;
}

// 1. Soporte para Servicio de Windows (nombre del servicio y app)
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Isg-Api-Pulso";
});

// 2. Controladores y Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddLogging();

// Configurar autenticación JWT (las tokens son emitidas por otra API de Auth)
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtIssuer = jwtSection["Issuer"];
var jwtAudience = jwtSection["Audience"];
var jwtKey = jwtSection["Key"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = false;
        // Token is signed using HS512 (symmetric). Configure symmetric validation using the shared key.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            // The issuer's tokens don't include an 'aud' claim; disable strict audience validation
            ValidateAudience = false,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // Custom audience validator: accept tokens with empty aud when issuer matches expected issuer
            AudienceValidator = (IEnumerable<string>? audiences, SecurityToken securityToken, TokenValidationParameters validationParameters) =>
            {
                // If the token has no audience claim, accept it (we already validate issuer and signature)
                if (audiences == null || !audiences.Any(a => !string.IsNullOrWhiteSpace(a)))
                {
                    return true;
                }

                // Otherwise, ensure one of the audiences matches the configured valid audience (case-insensitive)
                return audiences.Any(a => string.Equals(a, validationParameters.ValidAudience, StringComparison.OrdinalIgnoreCase));
            }
        };
        // Añadimos eventos para registrar detalles de por qué falla la validación JWT
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                try
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    var auth = context.Request.Headers["Authorization"].ToString();
                    if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = auth.Substring("Bearer ".Length).Trim();
                        var handler = new JwtSecurityTokenHandler();
                        var jwt = handler.ReadJwtToken(token);
                        var alg = jwt?.Header?.Alg ?? "(none)";
                        var kid = jwt?.Header?.Kid ?? "(none)";
                        logger.LogInformation("JWT header: alg={alg} kid={kid}", alg, kid);
                        try
                        {
                            var configuredIssuer = context.Options.TokenValidationParameters?.ValidIssuer ?? "(null)";
                            var configuredAudience = context.Options.TokenValidationParameters?.ValidAudience ?? "(null)";
                            var tokenIssuer = jwt?.Issuer ?? "(none)";
                            var tokenAudiences = jwt?.Audiences != null ? string.Join(',', jwt.Audiences) : "(none)";
                            logger.LogInformation("Configured ValidIssuer={configuredIssuer} ValidAudience={configuredAudience}", configuredIssuer, configuredAudience);
                            logger.LogInformation("Token issuer={tokenIssuer} audiences={tokenAudiences}", tokenIssuer, tokenAudiences);
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    try { context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer").LogWarning(ex, "No se pudo leer el token JWT en OnMessageReceived"); } catch { }
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                try
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    logger.LogError(context.Exception, "Autenticación JWT fallida");
                    // Imprimir excepción completa en consola para diagnóstico
                    try { Console.WriteLine(context.Exception.ToString()); } catch { }
                    // En Development añadimos un encabezado con el error para depuración
                    try
                    {
                        if (builder.Environment.IsDevelopment())
                        {
                            context.Response.Headers["X-Auth-Error"] = context.Exception.Message;
                        }
                    }
                    catch { }
                }
                catch { }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                try
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    var name = context.Principal?.Identity?.Name ?? context.Principal?.FindFirst("sub")?.Value;
                    logger.LogInformation("Token JWT validado para {name}", name);
                }
                catch { }
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                try
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
                    logger.LogWarning("JWT Challenge: error={error} description={description}", context.Error, context.ErrorDescription);
                }
                catch { }
                return Task.CompletedTask;
            }
        };
    });

// Configurar Swagger y habilitar esquema Bearer para la UI (botón Authorize)
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "isg-api-pulso", Version = "v1" });

    var securityScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Ingrese 'Bearer {token}' (sin comillas). Token emitido por el servicio de Auth.",
        Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });
});

// Registrar servicio para ejecución segura de Stored Procedures
builder.Services.AddScoped<isg_api_pulso.Services.ISqlEjecutorService, isg_api_pulso.Services.SqlEjecutorService>();

// 3. Configuración de CORS para el Frontend Next.js
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalFront", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Pipeline HTTP
app.UseStaticFiles();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowLocalFront");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();