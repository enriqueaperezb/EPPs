namespace EPPs.Models
{
    public class previoInventario_detalle
    {
        public string Codigo { get; set; }
        public string? CodigoArticulo { get; set; }
        public string Articulo { get; set; } 
        public decimal Cantidad { get; set; }
        public string? NombreCentroCosto { get; set; }
        public string? CodigoCentroCosto { get; set; } // d.codigo_efc

    }
}
