using Microsoft.AspNetCore.Mvc.Rendering;

namespace EPPs.Models
{
    public class previoInventario_detalle
    {
        public string Codigo { get; set; }
        public string? CodigoArticulo { get; set; }
        public string Articulo { get; set; } 
        public decimal Cantidad { get; set; }
        public string? CentroCosto { get; set; }
        public string? CodigoCentroCosto { get; set; } // d.codigo_efc
        public string? Estado { get; set; }
        public DateTime Entrega { get; set; }

    }
}
