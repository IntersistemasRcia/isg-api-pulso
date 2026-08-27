using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security;
using System.Threading.Tasks;
using System.Globalization;

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
        public async Task<IEnumerable<dynamic>> ListarSpArquitecturaAsync()
        {
            string connectionString = _config.GetConnectionString("IsgApiPulsoDb")
                ?? throw new InvalidOperationException("Cadena de conexión 'IsgApiPulsoDb' no configurada.");

            const string sql = @"SELECT 
    o.name AS NombreSP,
    m.definition AS CodigoSQL
FROM sys.sql_modules m
INNER JOIN sys.objects o ON m.object_id = o.object_id
WHERE o.type = 'P' 
  AND o.name LIKE 'sp_ISG_Vision_%'
ORDER BY o.name;";

            try
            {
                using IDbConnection db = new SqlConnection(connectionString);
                var resultado = await db.QueryAsync(sql);
                return resultado;
            }
            catch (SqlException sqlEx)
            {
                throw new InvalidOperationException("Error al consultar la arquitectura de Stored Procedures.", sqlEx);
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
