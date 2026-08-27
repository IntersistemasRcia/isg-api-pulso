using System;
using System.Text.Json;

namespace isg_api_pulso.Services
{
    public static class JsonElementConverter
    {
        public static object? NormalizarValor(object? valor)
        {
            if (valor is JsonElement element)
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => ParsearTextoOFecha(element.GetString()),
                    JsonValueKind.Number => (object)(element.TryGetInt64(out long l) ? l : element.GetDouble()),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => element.GetRawText()
                };
            }

            return valor ?? null;
        }

        private static object? ParsearTextoOFecha(string? texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;

            if (DateTime.TryParse(texto, out DateTime fecha))
            {
                return fecha;
            }

            return texto;
        }
    }
}
