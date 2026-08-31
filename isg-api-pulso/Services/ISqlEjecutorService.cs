using System.Collections.Generic;
using System.Threading.Tasks;

namespace isg_api_pulso.Services
{
    /// <summary>
    /// Interfaz para ejecutar Stored Procedures de forma segura.
    /// </summary>
    public interface ISqlEjecutorService
    {
        /// <summary>
        /// Ejecuta un Stored Procedure validado y retorna el resultado dinámico.
        /// </summary>
        /// <param name="nombreSp">Nombre completo del Stored Procedure (debe comenzar con el prefijo autorizado).</param>
        /// <param name="parametros">Parámetros opcionales para el SP.</param>
        Task<IEnumerable<dynamic>> EjecutarSpAsync(string nombreSp, Dictionary<string, object>? parametros = null);

        /// <summary>
        /// Lista la arquitectura de Stored Procedures autorizados (nombre y código SQL) consultando sys.sql_modules.
        /// </summary>
        Task<IEnumerable<dynamic>> ListarSpArquitecturaAsync(bool includeSql = false);
    }
}
