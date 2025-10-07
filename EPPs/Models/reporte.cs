namespace EPPs.Models
{
    public class ReporteFiltroVM
    {
        public string? Empresa { get; set; }
        public string? CodigoEmp { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }

        public List<ReporteGrupoVM> Grupos { get; set; } = new();
    }

    public class ReporteGrupoVM
    {
        public string CodigoCpi { get; set; } = "";
        public string? FotoUrl { get; set; } // /uploads/fotos/...
        public List<ReporteLineaVM> Lineas { get; set; } = new();
    }

    public class ReporteLineaVM
    {
        public DateTime FechaElaboCpi { get; set; }
        public string Articulo { get; set; } = "";
        public decimal Cantidad { get; set; }
    }
}
