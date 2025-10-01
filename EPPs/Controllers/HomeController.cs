using EPPs.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using System.Data;

namespace EPPs.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _config;

        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? codigo_emp)
        {
            var resultados = new List<previoInventario>();
            var connStr = _config.GetConnectionString("DefaultConnection");

            // EJEMPLO de consulta: ajusta nombres de tabla/columnas a tu base
            const string sql = @"
                SELECT
                    dbo.i_cab_prev_inve.codigo_cpi codigo,
                    dbo.r_empleado.nombre_emp + ' ' + dbo.r_empleado.nombre_emp empleado,
                    dbo.i_cab_prev_inve.fecha_elabo_cpi fecha,
                    ISNULL(dbo.i_cab_prev_inve.observacion_cpi,'') observacion
                FROM
                    dbo.i_cab_prev_inve INNER JOIN
                    dbo.r_empleado ON dbo.i_cab_prev_inve.codigo_emp = dbo.r_empleado.codigo_emp
                WHERE (dbo.i_cab_prev_inve.codigo_emp LIKE @codigo_emp) 
                ORDER BY dbo.i_cab_prev_inve.codigo_cpi DESC;";

            await using var conn = new SqlConnection(connStr);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@codigo_emp", SqlDbType.NVarChar, 200) { Value = (object?)codigo_emp ?? DBNull.Value });

            await conn.OpenAsync();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                resultados.Add(new previoInventario
                {
                    Codigo = rdr.GetString(0),
                    Empleado = rdr.GetString(1),
                    Fecha = rdr.GetDateTime(2),
                    Observacion = rdr.GetString(3)
                });
            }

            ViewBag.Codigo_emp = codigo_emp; // para rellenar el input en la vista
            return View(resultados);
        }

        [HttpGet]
        public async Task<IActionResult> previoInventario_detalle(string codigo_cpi)
        {
            var detalles = new List<previoInventario_detalle>();
            var connStr = _config.GetConnectionString("DefaultConnection");

            const string sql = @"
                SELECT 
                    dbo.i_det_prev_inve.codigo_dpv codigo,
                    dbo.c_articulo.nombre_art articulo,
                    dbo.i_det_prev_inve.cantidad_dpv cantidad
                FROM 
                    dbo.i_det_prev_inve INNER JOIN
                    dbo.c_articulo ON dbo.i_det_prev_inve.codigo_art = dbo.c_articulo.codigo_art 
                WHERE dbo.i_det_prev_inve.codigo_cpi = @codigo_cpi
                ORDER BY dbo.c_articulo.nombre_art;";

            await using var conn = new SqlConnection(connStr);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@codigo_cpi", SqlDbType.Int) { Value = codigo_cpi });

            await conn.OpenAsync();
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                detalles.Add(new previoInventario_detalle
                {
                    Codigo = rdr.GetString(0),
                    Articulo = rdr.GetString(1),
                    Cantidad = rdr.GetDecimal(2)
                });
            }

            // Devolvemos un parcial que renderiza solo la tabla detalle
            return PartialView("_previoInventario_detalle", detalles);
        }
    }
}
