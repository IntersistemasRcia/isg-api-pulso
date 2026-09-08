using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security;
using System.Threading.Tasks;
using System.Globalization;
using System.Text.RegularExpressions;
using isg_api_pulso.Models;

namespace isg_api_pulso.Services
{
    /// <summary>
    /// Implementación del servicio que valida y ejecuta Stored Procedures usando Dapper.
    /// </summary>
    public class SqlEjecutorService : ISqlEjecutorService
    {
        private readonly IConfiguration _config;
        private const string PrefijoAutorizado = "sp_ISG_Vision_";

        public SqlEjecutorService(IConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Consulta sys.sql_modules para obtener nombre y código de los Stored Procedures que cumplen con el prefijo autorizado.
        /// </summary>
        public async Task<IEnumerable<SpArquitecturaDto>> ListarSpArquitecturaAsync(bool includeSql = false)
        {
            string connectionString = _config.GetConnectionString("IsgApiPulsoDb")
                ?? throw new InvalidOperationException("Cadena de conexión 'IsgApiPulsoDb' no configurada.");
            // Query que lista procedimientos, su definición (si existe) y parámetros (si existen)
            const string sqlAll = @"
SELECT
    o.name AS NombreSP,
    m.definition AS CodigoSQL,
    p.name AS NombreParametro,
    t.name AS TipoParametro,
    p.is_output AS EsOutput,
    p.has_default_value AS TieneDefault,
    p.parameter_id AS Orden
FROM sys.procedures o
LEFT JOIN sys.sql_modules m ON m.object_id = o.object_id
LEFT JOIN sys.parameters p ON p.object_id = o.object_id
LEFT JOIN sys.types t ON p.user_type_id = t.user_type_id
WHERE o.name LIKE 'sp_ISG_Vision_%'
ORDER BY o.name, p.parameter_id;";

            try
            {
                using IDbConnection db = new SqlConnection(connectionString);

                var rows = await db.QueryAsync(sqlAll);

                // Agrupar por NombreSP y construir DTO tipado
                var dict = new Dictionary<string, SpArquitecturaDto>(StringComparer.OrdinalIgnoreCase);
                var defaultsMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in rows)
                {
                    string sp = r.NombreSP;
                    if (!dict.ContainsKey(sp))
                    {
                        string? codigo = r.CodigoSQL;
                        // Analizar firma del SP en memoria para detectar parámetros con valor por defecto
                        var defaults = ExtractParamsWithDefaultFromDefinition(codigo);
                        defaultsMap[sp] = defaults;

                        dict[sp] = new SpArquitecturaDto
                        {
                            NombreSp = sp,
                            Descripcion = TryExtractPulsoComment(codigo),
                            Parametros = new List<ParametroDto>(),
                            CodigoSQL = includeSql ? codigo : null
                        };
                    }

                    // Si tiene parámetro (puede ser null cuando el SP no tiene params)
                    if (r.NombreParametro != null)
                    {
                        var nombreParam = r.NombreParametro as string ?? string.Empty;
                        if (nombreParam.StartsWith("@")) nombreParam = nombreParam.Substring(1);

                        bool esOutput = (r.EsOutput ?? false);
                        defaultsMap.TryGetValue(sp, out var defaultsForSp);
                        bool tieneDefaultFromSql = defaultsForSp != null && defaultsForSp.Contains(nombreParam);
                        bool tieneDefault = (r.TieneDefault ?? false) || tieneDefaultFromSql;

                        dict[sp].Parametros.Add(new ParametroDto
                        {
                            Nombre = nombreParam,
                            Tipo = r.TipoParametro,
                            TieneDefault = tieneDefault,
                            Requerido = !tieneDefault && !esOutput,
                            EsOutput = esOutput
                        });
                    }
                }

                return dict.Values;
            }
            catch (SqlException sqlEx)
            {
                throw new InvalidOperationException("Error al consultar la arquitectura de Stored Procedures.", sqlEx);
            }
        }

        private static string? TryExtractPulsoComment(string? definition)
        {
            if (string.IsNullOrWhiteSpace(definition)) return null;

            try
            {
                // Buscar la primera línea que contenga el comentario '-- Pulso: ...' (case-insensitive)
                var lines = definition.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var rx = new Regex("^\\s*--\\s*Pulso\\s*:\\s*(.+)$", RegexOptions.IgnoreCase);

                foreach (var line in lines)
                {
                    var m = rx.Match(line);
                    if (m.Success)
                    {
                        var text = m.Groups[1].Value.Trim();
                        if (string.IsNullOrEmpty(text)) return null;
                        if (text.Length > 500) text = text.Substring(0, 500);
                        return text;
                    }
                }

                return null;
            }
            catch
            {
                // Nunca propagar la excepción desde el extractor; en caso de error devolver null
                return null;
            }
        }

        /// <summary>
        /// Extrae los nombres de parámetros que en la firma del Stored Procedure declaran un valor por defecto.
        /// Analiza el código entre CREATE/ALTER PROC ... y AS, eliminando comentarios simples y de bloque.
        /// Retorna un conjunto de nombres de parámetros sin '@' en minúsculas.
        /// </summary>
        private static HashSet<string> ExtractParamsWithDefaultFromDefinition(string? definition)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(definition)) return result;

            try
            {
                // Eliminar comentarios de bloque /* */ y comentarios de línea --
                string noBlock = Regex.Replace(definition, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                var lines = noBlock.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new System.Text.StringBuilder();

                foreach (var raw in lines)
                {
                    var line = raw;
                    var idx = line.IndexOf("--");
                    if (idx >= 0) line = line.Substring(0, idx);
                    sb.AppendLine(line);
                }

                var cleaned = sb.ToString();

                // Localizar inicio de la firma: CREATE PROCEDURE o ALTER PROCEDURE
                var rxProc = new Regex("\\b(CREATE|ALTER)\\s+(PROCEDURE|PROC)\\b", RegexOptions.IgnoreCase);
                var mProc = rxProc.Match(cleaned);
                if (!mProc.Success) return result;

                // Tomar texto desde el match hasta el primer occurrence de '\nAS\b' o '\\nBEGIN\\b' o '\nAS\s'
                var after = cleaned.Substring(mProc.Index + mProc.Length);
                var rxAs = new Regex("\\bAS\\b", RegexOptions.IgnoreCase);
                var matchAs = rxAs.Match(after);
                string signaturePart = matchAs.Success ? after.Substring(0, matchAs.Index) : after;

                // Ahora buscar parámetros en la signaturePart: patrones como @ParamName Tipo ... = <valor>
                var rxParam = new Regex("(@[A-Za-z0-9_]+)\\s+[A-Za-z0-9_()\\.]+(?:\\s*=[^,)]*)?", RegexOptions.IgnoreCase);
                var rxDefault = new Regex("(@[A-Za-z0-9_]+)\\s+[A-Za-z0-9_()\\.]+\\s*=", RegexOptions.IgnoreCase);

                foreach (Match m in rxParam.Matches(signaturePart))
                {
                    var name = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(name)) continue;
                    // Determinar si tiene '=' después del tipo (es decir, default)
                    if (rxDefault.IsMatch(m.Value))
                    {
                        var cleanName = name.StartsWith("@") ? name.Substring(1) : name;
                        result.Add(cleanName);
                    }
                }

                return result;
            }
            catch
            {
                // Nunca fallar el endpoint por errores en el parseo; simplemente no marcar defaults adicionales
                return result;
            }
        }

        public async Task<IEnumerable<dynamic>> EjecutarSpAsync(string nombreSp, Dictionary<string, object>? parametros = null)
        {
            if (string.IsNullOrWhiteSpace(nombreSp))
                throw new ArgumentException("El nombre del Stored Procedure no puede estar vacío.", nameof(nombreSp));

            // 1. Validación estricta del prefijo autorizado
            if (!nombreSp.StartsWith(PrefijoAutorizado, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException($"El Stored Procedure '{nombreSp}' no está autorizado para ser ejecutado.");

            // 2. Sanitizar el nombre del SP: permitir solo letras, números, guiones bajos y punto (schema)
            //    Evitar que contenga caracteres peligrosos.
            var nombreSanitizado = SanitizeStoredProcedureName(nombreSp);
            if (!string.Equals(nombreSanitizado, nombreSp, StringComparison.Ordinal))
                throw new ArgumentException("El nombre del Stored Procedure contiene caracteres no válidos.");

            string connectionString = _config.GetConnectionString("IsgApiPulsoDb")
                ?? throw new InvalidOperationException("Cadena de conexión 'IsgApiPulsoDb' no configurada.");

            try
            {
                using IDbConnection db = new SqlConnection(connectionString);

                // Preparar parámetros de forma segura usando DynamicParameters
                DynamicParameters dp = new DynamicParameters();
                if (parametros != null)
                {
                    foreach (var kvp in parametros)
                    {
                        var name = kvp.Key;
                        var value = kvp.Value;

                        // Normalizar JsonElement u otros valores a tipos CLR adecuados
                        var valorNormalizado = JsonElementConverter.NormalizarValor(value);

                        // Dapper espera el nombre del parámetro sin '@'
                        var paramName = name.StartsWith("@") ? name.Substring(1) : name;

                        // Añadir parámetro con DbType cuando sea posible para evitar conversiones inesperadas
                        if (valorNormalizado is DateTime dt)
                        {
                            dp.Add(paramName, dt, dbType: DbType.Date);
                        }
                        else if (valorNormalizado is int i)
                        {
                            dp.Add(paramName, i, dbType: DbType.Int32);
                        }
                        else if (valorNormalizado is long l)
                        {
                            dp.Add(paramName, l, dbType: DbType.Int64);
                        }
                        else if (valorNormalizado is double d)
                        {
                            dp.Add(paramName, d, dbType: DbType.Double);
                        }
                        else if (valorNormalizado is bool b)
                        {
                            dp.Add(paramName, b, dbType: DbType.Boolean);
                        }
                        else if (valorNormalizado == null)
                        {
                            dp.Add(paramName, null);
                        }
                        else
                        {
                            dp.Add(paramName, valorNormalizado);
                        }
                    }
                }

                // Ejecutar el SP de forma segura con Dapper
                var resultado = await db.QueryAsync(
                    nombreSanitizado,
                    dp,
                    commandType: CommandType.StoredProcedure
                );

                return resultado;
            }
            catch (SqlException sqlEx)
            {
                // Registrar o manejar la excepción según política de la organización.
                // Para este ejemplo, relanzamos con información controlada.
                throw new InvalidOperationException("Error al ejecutar el Stored Procedure en la base de datos.", sqlEx);
            }
        }

        private static string SanitizeStoredProcedureName(string nombre)
        {
            // Permitir schema.nombre o solo nombre. Validar que cada segmento solo contenga caracteres permitidos.
            var partes = nombre.Split('.');
            for (int i = 0; i < partes.Length; i++)
            {
                var parte = partes[i];
                if (string.IsNullOrWhiteSpace(parte)) return string.Empty;

                foreach (char c in parte)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '_' ))
                        return string.Empty;
                }
            }

            // Retornar el nombre tal cual si pasó la validación
            return nombre;
        }
    }
}
