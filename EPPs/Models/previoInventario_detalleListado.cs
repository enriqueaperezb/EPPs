using Microsoft.AspNetCore.Mvc.Rendering;

namespace EPPs.Models
{
    public class previoInventario_detalleListado
    {
        public List<previoInventario_detalle> Detalles { get; set; } = new();
        public List<SelectListItem> Articulos { get; set; } = new();
        public List<SelectListItem> CentrosCosto { get; set; } = new();
    }
}
