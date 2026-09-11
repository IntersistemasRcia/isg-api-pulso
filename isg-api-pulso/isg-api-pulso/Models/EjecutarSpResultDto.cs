using System.Collections.Generic;

namespace isg_api_pulso.Models
{
    public class EjecutarSpResultDto
    {
        public bool Ok { get; set; } = true;
        public IEnumerable<Dictionary<string, object?>> Rows { get; set; } = new List<Dictionary<string, object?>>();
        public int TotalRows { get; set; }
        public bool Truncated { get; set; }
        public int? LimiteFilas { get; set; }
    }
}
