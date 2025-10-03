namespace EPPs.Models
{

    // DTOs para el guardado
    public class GuardarDetallePrevioInventario
    {
        public string? CodigoCpi { get; set; } = null;
        public List<ItemCambio> Items { get; set; } = new();
        public List<string> TodosCodigos { get; set; } = new();
        public List<ItemCambio> Nuevos { get; set; } = new();
        public string? FotoBase64 { get; set; }  // <-- NUEVO
    }

    public class ItemCambio
    {
        public string Codigo { get; set; } = "";           // codigo_dpv
        public string? CodigoArticulo { get; set; }        // codigo_art (nullable si permites sin artículo)
        public decimal Cantidad { get; set; }              // cantidad_dpv
    }
}
