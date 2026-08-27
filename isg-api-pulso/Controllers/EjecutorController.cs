using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using isg_api_pulso.Services;
using Microsoft.AspNetCore.Authorization;


namespace isg_api_pulso.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/v1/pulso")]
    public class EjecutorController : ControllerBase
    {
        private readonly ISqlEjecutorService _ejecutorService;

        public EjecutorController(ISqlEjecutorService ejecutorService)
        {
            _ejecutorService = ejecutorService;
        }

        /// <summary>
        /// Devuelve la arquitectura (nombre y código) de los Stored Procedures publicados para la IA.
        /// Consulta internamente sys.sql_modules filtrando por 'sp_ISG_Vision_%'.
        /// </summary>
        [HttpGet("SPs_arquitectura")]
        public async Task<IActionResult> ObtenerSpArquitectura()
        {
            try
            {
                var resultado = await _ejecutorService.ListarSpArquitecturaAsync();
                return Ok(resultado);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Error al obtener la arquitectura de Stored Procedures.", detalle = ex.Message });
            }
        }

        /// <summary>
        /// Endpoint que ejecuta Stored Procedures autorizados y retorna su resultado.
        /// Regla de negocio: solo se permiten SP que comiencen con 'sp_ISG_Vision_'.
        /// </summary>
        [HttpPost("ejecutar-sp")]
        public async Task<IActionResult> EjecutarStoredProcedure([FromBody] PeticionSpDto peticion)
        {
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.NombreSp))
                return BadRequest(new { error = "Petición inválida: se requiere 'NombreSp'." });

            try
            {
                // Normalize parameter values: convert System.Text.Json.JsonElement to CLR types
                Dictionary<string, object>? parametros = null;
                if (peticion.Parametros != null)
                {
                    parametros = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in peticion.Parametros)
                    {
                        var name = kv.Key;
                        var value = kv.Value;

                        if (value is System.Text.Json.JsonElement je)
                        {
                            switch (je.ValueKind)
                            {
                                case System.Text.Json.JsonValueKind.String:
                                    parametros[name] = je.GetString()!;
                                    break;
                                case System.Text.Json.JsonValueKind.Number:
                                    if (je.TryGetInt32(out var i)) parametros[name] = i;
                                    else if (je.TryGetInt64(out var l)) parametros[name] = l;
                                    else if (je.TryGetDouble(out var d)) parametros[name] = d;
                                    else parametros[name] = je.GetRawText();
                                    break;
                                case System.Text.Json.JsonValueKind.True:
                                case System.Text.Json.JsonValueKind.False:
                                    parametros[name] = je.GetBoolean();
                                    break;
                                case System.Text.Json.JsonValueKind.Null:
                                    parametros[name] = null!;
                                    break;
                                default:
                                    // object or array -> pass raw JSON
                                    parametros[name] = je.GetRawText();
                                    break;
                            }
                        }
                        else
                        {
                            parametros[name] = value!;
                        }
                    }
                }

                var resultado = await _ejecutorService.EjecutarSpAsync(peticion.NombreSp, parametros);
                return Ok(resultado);
            }
            catch (ArgumentException argEx)
            {
                return BadRequest(new { error = argEx.Message });
            }
            catch (System.Security.SecurityException secEx)
            {
                return BadRequest(new { error = secEx.Message });
            }
            catch (Exception ex)
            {
                // Incluir detalle de la excepción interna cuando exista para facilitar depuración en desarrollo
                var detalle = ex.InnerException?.Message ?? ex.Message;
                return StatusCode(500, new { error = "Error al ejecutar el Stored Procedure.", detalle });
            }
        }
    }

    public class PeticionSpDto
    {
        // Nombre completo del Stored Procedure. Debe comenzar con 'sp_ISG_Vision_'.
        public string NombreSp { get; set; } = string.Empty;

        // Parámetros opcionales para el SP.
        public Dictionary<string, object>? Parametros { get; set; }
    }
}
