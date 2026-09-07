using System.Collections.Generic;

namespace isg_api_pulso.Models
{
    public class SpArquitecturaDto
    {
        public string NombreSp { get; set; } = string.Empty;

        // Descripción extraída del comentario '-- Pulso: ...' en la definición del SP (máx 500 chars)
        public string? Descripcion { get; set; }

        // Parámetros del SP (vacío si no tiene parámetros)
        public List<ParametroDto> Parametros { get; set; } = new List<ParametroDto>();

        // Incluido solo cuando includeSql=true en la consulta
        public string? CodigoSQL { get; set; }
    }

    public class ParametroDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string? Tipo { get; set; }
        public bool Requerido { get; set; }
        public bool EsOutput { get; set; }
    }
}
