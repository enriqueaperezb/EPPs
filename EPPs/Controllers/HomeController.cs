using EPPs.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Reflection;

namespace EPPs.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _config;
        private readonly ILogger<HomeController> _logger;
        private readonly IWebHostEnvironment _env; 

        private string? _connection;            //Nombre de la conexión a usar
        private string? _codigo_nef;            //Nivel del centro de costo
        private string? _codigo_epi_aprobado;   //Estado previo inventario
        private string? _codigo_epi_anulado;    //Estado previo inventario
        private string? _codigo_usu_aprueba;    //Usuario
        private string? _codigo_tti_consumo;    //Tipo de comprobante de inventario

        public HomeController(IConfiguration config, ILogger<HomeController> logger, IWebHostEnvironment env)
        {
            _config = config;
            _logger = logger;
            _env = env;             
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
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            SetVarsEmpresaFromCookie(); // ← lee cookie y setea variables de códigos Venture
            base.OnActionExecuting(context);
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? codigo_emp, bool reset = false)
        {
            if (reset)
            {
                ViewBag.Codigo_emp = "";
                ViewBag.NombreEmpleado = "";
                ViewBag.Empresa = Request.Cookies["empresa"] ?? "";
                return View(new List<previoInventario>()); // ← sin datos
            }

            var resultados = new List<previoInventario>();
            var connStr = _config.GetConnectionString(_connection);

            // Listamos los previos inventarios, EPP, tipo consumo, del empleado 
            const string sql = @"
                SELECT
                    i_cab_prev_inve.codigo_cpi codigo,
                    i_cab_prev_inve.fecha_elabo_cpi fecha,
                    ISNULL(dbo.i_cab_prev_inve.observacion_cpi,'') observacion,
                    dbo.i_est_prev_inve.nombre_epi estado
                FROM
                    dbo.i_cab_prev_inve INNER JOIN
                    dbo.i_est_prev_inve ON dbo.i_est_prev_inve.codigo_epi = dbo.i_cab_prev_inve.codigo_epi
                WHERE 
                    (dbo.i_cab_prev_inve.codigo_emp LIKE '00' + RIGHT(@codigo_emp,LEN(@codigo_emp)-2)) AND
                    --(dbo.i_cab_prev_inve.codigo_epi not like @codigo_epi) AND
                    (dbo.i_cab_prev_inve.observacion_cpi LIKE 'EPP%') 
                    AND dbo.i_cab_prev_inve.codigo_tti LIKE @codigo_tti
                ORDER BY 
                    i_cab_prev_inve.codigo_cpi DESC;";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.Add(new SqlParameter("@codigo_emp", SqlDbType.NVarChar, 200) { Value = (object?)codigo_emp ?? DBNull.Value });
                //cmd.Parameters.Add(new SqlParameter("@codigo_epi", SqlDbType.NVarChar, 200) { Value = (object?)_codigo_epi_aprobado ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@codigo_tti", SqlDbType.NVarChar, 200) { Value = (object?)_codigo_tti_consumo ?? DBNull.Value });
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    resultados.Add(new previoInventario
                    {
                        Codigo = rdr.GetString(0),
                        Fecha = rdr.GetDateTime(1),
                        Observacion = rdr.GetString(2),
                        Estado = rdr.GetString(3)
                    });
                }
            }

            //Nombre del empleado (para mostrar al buscar)
            string nombreEmpleado = "";
            if (!string.IsNullOrWhiteSpace(codigo_emp))
            {
                const string sqlNombre = @"
                    SELECT TOP (1) 
                        dbo.r_empleado.apellido_emp + ' ' + dbo.r_empleado.nombre_emp
                    FROM 
                        dbo.r_empleado 
                    WHERE 
                        dbo.r_empleado.codigo_emp LIKE '00' + RIGHT(@codigo_emp,LEN(@codigo_emp)-2);";

                await using var cmd2 = new SqlCommand(sqlNombre, conn);
                cmd2.Parameters.Add(new SqlParameter("@codigo_emp", SqlDbType.NVarChar, 200) { Value = codigo_emp });
                var obj = await cmd2.ExecuteScalarAsync();
                nombreEmpleado = obj as string ?? "";
            }

            ViewBag.Codigo_emp = codigo_emp; // para rellenar el input en la vista
            // Nuevo: el nombre a mostrar bajo el campo de código
            ViewBag.NombreEmpleado = nombreEmpleado;

            // Lee cookie de empresa para preseleccionar en la vista
            var empresaCookie = Request.Cookies["empresa"];
            ViewBag.Empresa = empresaCookie ?? "";

            return View(resultados);
        }

        [HttpGet]
        public async Task<IActionResult> previoInventario_detalle(string codigo_cpi)
        {
            var detalles = new List<previoInventario_detalle>();
            var articulos = new List<SelectListItem>();
            var connStr = _config.GetConnectionString(_connection);

            const string sqlDetalles = @"
                SELECT 
                    dbo.i_det_prev_inve.codigo_dpv codigo,
                    dbo.c_articulo.codigo_art codigo_art,
                    dbo.c_articulo.nombre_art articulo,
                    dbo.i_det_prev_inve.cantidad_dpv cantidad,
                    dbo.i_det_prev_inve.codigo_efc,
                    dbo.i_est_prev_inve.nombre_epi estado,
                    dbo.i_cab_prev_inve.fecha_efect_cpi entrega,
                    dbo.d_est_fisi_cost.nombre_efc CentroCosto
                FROM 
                    dbo.i_det_prev_inve INNER JOIN
                    dbo.c_articulo ON dbo.i_det_prev_inve.codigo_art = dbo.c_articulo.codigo_art INNER JOIN
                    dbo.i_cab_prev_inve ON dbo.i_cab_prev_inve.codigo_cpi = dbo.i_det_prev_inve.codigo_cpi INNER JOIN
                    dbo.i_est_prev_inve ON dbo.i_est_prev_inve.codigo_epi = dbo.i_cab_prev_inve.codigo_epi LEFT OUTER JOIN
                    dbo.d_est_fisi_cost ON dbo.d_est_fisi_cost.codigo_efc = dbo.i_det_prev_inve.codigo_efc
                WHERE 
                    dbo.i_det_prev_inve.codigo_cpi = @codigo_cpi
                ORDER BY 
                    dbo.c_articulo.nombre_art;";

            const string sqlArticulos = @"
                SELECT 
                    codigo_art, nombre_art
                FROM 
                    dbo.c_articulo
                ORDER BY 
                    nombre_art;";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // Detalles
            await using (var cmd = new SqlCommand(sqlDetalles, conn))
            {
                cmd.Parameters.Add(new SqlParameter("@codigo_cpi", SqlDbType.NVarChar, 50) { Value = codigo_cpi });
                await using var rdr = await cmd.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                {
                    detalles.Add(new previoInventario_detalle
                    {
                        Codigo = rdr.GetString(0),
                        CodigoArticulo = rdr.GetString(1),   // <-- Nuevo, se llena el código
                        Articulo = rdr.GetString(2),
                        Cantidad = rdr.GetDecimal(3),
                        CodigoCentroCosto = rdr.IsDBNull(4) ? null : rdr.GetString(4),
                        Estado = rdr.IsDBNull(4) ? null : rdr.GetString(5),
                        Entrega = rdr.GetDateTime(6),
                        CentroCosto = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    });
                }
            }

            // Artículos
            await using (var cmd2 = new SqlCommand(sqlArticulos, conn))
            await using (var rdr2 = await cmd2.ExecuteReaderAsync())
            {
                while (await rdr2.ReadAsync())
                {
                    articulos.Add(new SelectListItem
                    {
                        Value = rdr2.GetString(0), // codigo_art
                        Text = rdr2.GetString(1)   // nombre_art
                    });
                }
            }

            const string sqlEfc = @"
                SELECT 
                    codigo_efc, nombre_efc
                FROM 
                    dbo.d_est_fisi_cost
                WHERE
                    codigo_nef = @codigo_nef
                ORDER BY 
                    nombre_efc;"; 
            
            var centros = new List<SelectListItem>();

            await using (var cmd3 = new SqlCommand(sqlEfc, conn))
            {
                // Agregas el parámetro correctamente
                cmd3.Parameters.Add(new SqlParameter("@codigo_nef", SqlDbType.NVarChar, 200)
                {
                    Value = (object?)_codigo_nef ?? DBNull.Value
                });

                await using (var r3 = await cmd3.ExecuteReaderAsync())
                {
                    while (await r3.ReadAsync())
                    {
                        centros.Add(new SelectListItem
                        {
                            Value = r3.GetString(0),
                            Text = r3.GetString(1)
                        });
                    }
                }
            }
            var vm = new previoInventario_detalleListado
            {
                Detalles = detalles,
                Articulos = articulos,
                CentrosCosto = centros
            };

            return PartialView("_previoInventario_detalle", vm);
        }

        private static string SanitizarNombreArchivo(string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return "empleado";
            // Reemplaza espacios por _
            var s = nombre.Trim().Replace(' ', '_');

            // Quita caracteres inválidos de nombre de archivo
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c.ToString(), "");

            return s;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GuardarPrevioDetalle([FromBody] GuardarDetallePrevioInventario req)
        {
            if (req == null) return BadRequest("Petición vacía.");

            var connStr = _config.GetConnectionString(_connection);

            // 1) Estado del cabecero
            const string sqlEstado = @"SELECT TOP 1 e.nombre_epi
                               FROM dbo.i_cab_prev_inve
                               LEFT JOIN dbo.i_est_prev_inve e ON e.codigo_epi = dbo.i_cab_prev_inve.codigo_epi
                               WHERE dbo.i_cab_prev_inve.codigo_cpi = @codigo_cpi;";
            await using var cn1 = new SqlConnection(_config.GetConnectionString(_connection));
            await cn1.OpenAsync();
            await using (var cmdE = new SqlCommand(sqlEstado, cn1))
            {
                cmdE.Parameters.Add(new SqlParameter("@codigo_cpi", SqlDbType.NVarChar, 50) { Value = req.CodigoCpi });
                var estado = (string?)await cmdE.ExecuteScalarAsync() ?? "";
                if (estado.Equals("Aprobado", StringComparison.OrdinalIgnoreCase))
                {
                    return StatusCode(400, new { message = "El cabecero está Aprobado. No se pueden modificar los detalles." });
                }
            }

            //Guardamos si no es de lectura
            const string SQL_UPD_DET = @"
                UPDATE 
                    dbo.i_det_prev_inve
                SET 
                    codigo_art = @codigo_art,
                    cantidad_dpv = @cantidad,
                    canti_pedid_dpv = @cantidad,
                    codigo_efc = @codigo_efc
                WHERE 
                    codigo_dpv = @codigo_dpv;";

            const string SQL_INS_DET = @"
                INSERT INTO 
                    dbo.i_det_prev_inve (codigo_cpi, codigo_dpv, codigo_art, cantidad_dpv, canti_pedid_dpv, precio_dpv, codigo_efc)
                VALUES 
                    (@codigo_cpi, (SELECT '00'+CONVERT(VARCHAR,MAX_NUM_BLO+1) FROM S_BLOQUEO WHERE TABLA_BLO LIKE 'i_det_prev_inve'), @codigo_art, @cantidad, @cantidad, 0, @codigo_efc);

                UPDATE 
                    S_BLOQUEO 
                SET 
                    MAX_NUM_BLO=MAX_NUM_BLO+1 FROM S_BLOQUEO WHERE TABLA_BLO LIKE 'i_det_prev_inve';";

            // 1) Normaliza colecciones
            var itemsSel = req.Items ?? new List<ItemCambio>();
            var codigosAll = req.TodosCodigos ?? new List<string>();
            var nuevos = req.Nuevos ?? new List<ItemCambio>();

            // 2) Regla de estado
            //    - Si NO hay seleccionados NI nuevos => 0012 (no borrar nada)
            //    - Si hay seleccionados o nuevos    => 0011 (y se borran los no seleccionados)
            var haySeleccion = (itemsSel.Count + nuevos.Count) > 0;
            var estadoEpi = haySeleccion ? _codigo_epi_aprobado : _codigo_epi_anulado;

            int updated = 0, inserted = 0, deleted = 0, headersUpdated = 0;

            await using var cn = new SqlConnection(connStr);
            await cn.OpenAsync();
            await using var tx = await cn.BeginTransactionAsync();

            // Preparamos lista de cabeceros involucrados (a partir de lo visible)
            var headerIds = new List<string>();
            try
            {
                if (codigosAll.Count > 0)
                {
                    var inParams = string.Join(",", codigosAll.Select((_, i) => $"@cd{i}"));
                    var sqlHdr = $@"
                        SELECT 
                            DISTINCT d.codigo_cpi
                        FROM 
                            dbo.i_det_prev_inve AS d
                        WHERE 
                            d.codigo_dpv IN ({inParams});";

                    await using (var cmdHdr = new SqlCommand(sqlHdr, cn, (SqlTransaction)tx))
                    {
                        for (int i = 0; i < codigosAll.Count; i++)
                            cmdHdr.Parameters.Add(new SqlParameter($"@cd{i}", SqlDbType.NVarChar, 50) { Value = codigosAll[i] });

                        await using var rdr = await cmdHdr.ExecuteReaderAsync();
                        while (await rdr.ReadAsync())
                            headerIds.Add(rdr.GetString(0));
                    }
                }

                // 3) INSERT nuevos (requiere codigo_cpi)
                if (nuevos.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(req.CodigoCpi))
                        return BadRequest("Falta codigo_cpi para insertar nuevas líneas.");

                    foreach (var n in nuevos)
                    {
                        await using var cmdIns = new SqlCommand(SQL_INS_DET, cn, (SqlTransaction)tx);
                        cmdIns.Parameters.Add(new SqlParameter("@codigo_cpi", SqlDbType.NVarChar, 50) { Value = req.CodigoCpi });
                        cmdIns.Parameters.Add(new SqlParameter("@codigo_art", SqlDbType.NVarChar, 50) { Value = (object?)n.CodigoArticulo ?? DBNull.Value });

                        var pCant = cmdIns.Parameters.Add("@cantidad", SqlDbType.Decimal);
                        pCant.Precision = 22; pCant.Scale = 15;
                        pCant.Value = n.Cantidad;
                        cmdIns.Parameters.Add(new SqlParameter("@codigo_efc", SqlDbType.NVarChar, 50) { Value = (object?)n.CodigoCentroCosto ?? DBNull.Value });

                        inserted += await cmdIns.ExecuteNonQueryAsync() - 1;
                    }
                }

                // 4) UPDATE seleccionados
                foreach (var it in itemsSel)
                {
                    await using var cmdUp = new SqlCommand(SQL_UPD_DET, cn, (SqlTransaction)tx);
                    cmdUp.Parameters.Add(new SqlParameter("@codigo_art", SqlDbType.NVarChar, 50) { Value = (object?)it.CodigoArticulo ?? DBNull.Value });

                    var pCant = cmdUp.Parameters.Add("@cantidad", SqlDbType.Decimal);
                    pCant.Precision = 22; pCant.Scale = 15;
                    pCant.Value = it.Cantidad;

                    cmdUp.Parameters.Add(new SqlParameter("@codigo_dpv", SqlDbType.NVarChar, 50) { Value = it.Codigo });
                    cmdUp.Parameters.Add(new SqlParameter("@codigo_efc", SqlDbType.NVarChar, 50) { Value = (object?)it.CodigoCentroCosto ?? DBNull.Value });
                    
                    updated += await cmdUp.ExecuteNonQueryAsync();
                }

                // 5) DELETE no seleccionados (solo si hay selección)
                if (haySeleccion && codigosAll.Count > 0)
                {
                    var setSel = new HashSet<string>(itemsSel.Select(x => x.Codigo));
                    // si agregaste nuevos con código fijo, no estarán en codigosAll hasta que recargues desde BD,
                    // así que el delete no los tocará (correcto).
                    var paraEliminar = codigosAll.Where(c => !setSel.Contains(c)).ToList();
                    if (paraEliminar.Count > 0)
                    {
                        var inDel = string.Join(",", paraEliminar.Select((_, i) => $"@del{i}"));
                        var sqlDel = $"DELETE FROM dbo.i_det_prev_inve WHERE codigo_dpv IN ({inDel});";

                        await using var cmdDel = new SqlCommand(sqlDel, cn, (SqlTransaction)tx);
                        for (int i = 0; i < paraEliminar.Count; i++)
                            cmdDel.Parameters.Add(new SqlParameter($"@del{i}", SqlDbType.NVarChar, 50) { Value = paraEliminar[i] });

                        deleted = await cmdDel.ExecuteNonQueryAsync();
                    }
                }

                // 6) Actualizar estado de cabecero(s) (0011/0012)
                if (headerIds.Count > 0)
                {
                    var inHdr = string.Join(",", headerIds.Select((_, i) => $"@h{i}"));
                    var sqlUpdHdr = $@"
                        UPDATE 
                            dbo.i_cab_prev_inve
                        SET 
                            codigo_epi = @epi,
                            s_u_codigo_usu = @codigo_usu,
                            obser_tribu_cpi = 'Documento actualizado desde Tablet el ' + convert(varchar,getdate()),
                            fecha_efect_cpi = getdate()
                        WHERE 
                            codigo_cpi IN ({inHdr});";

                    await using var cmdHdrUpd = new SqlCommand(sqlUpdHdr, cn, (SqlTransaction)tx);
                    cmdHdrUpd.Parameters.Add(new SqlParameter("@epi", SqlDbType.NVarChar, 10) { Value = estadoEpi });
                    cmdHdrUpd.Parameters.Add(new SqlParameter("@codigo_usu", SqlDbType.NVarChar, 3) { Value = _codigo_usu_aprueba });
                    for (int i = 0; i < headerIds.Count; i++)
                        cmdHdrUpd.Parameters.Add(new SqlParameter($"@h{i}", SqlDbType.NVarChar, 50) { Value = headerIds[i] });

                    headersUpdated = await cmdHdrUpd.ExecuteNonQueryAsync();
                }

                // ------------------------------------------------------------------
                // GUARDAR FOTO (si llegó) + ACTUALIZAR pdf_normal_cpi en cabecera
                // ------------------------------------------------------------------
                string? fotoPathRel = null;

                if (!string.IsNullOrWhiteSpace(req.FotoBase64) && !string.IsNullOrWhiteSpace(req.CodigoCpi))
                {
                    // 2.1 Obtener nombre del empleado por codigo_cpi
                    string nombreEmpleado = "empleado";
                    const string SQL_EMPLEADO = @"
                        SELECT TOP (1) 
                            emp.apellido_emp + ' ' + emp.nombre_emp
                        FROM 
                            dbo.i_cab_prev_inve cpi
                            INNER JOIN dbo.r_empleado emp ON emp.codigo_emp = cpi.codigo_emp
                        WHERE 
                            cpi.codigo_cpi = @codigo_cpi;";

                    await using (var cmdEmp = new SqlCommand(SQL_EMPLEADO, cn, (SqlTransaction)tx))
                    {
                        cmdEmp.Parameters.Add(new SqlParameter("@codigo_cpi", SqlDbType.NVarChar, 50) { Value = req.CodigoCpi });
                        var obj = await cmdEmp.ExecuteScalarAsync();
                        if (obj is string s && !string.IsNullOrWhiteSpace(s)) nombreEmpleado = s;
                    }

                    // 2.2 Armar nombre de archivo: NombreEmpleado_sin_espacios + "_" + codigo_cpi + ".jpg"
                    var fileSafeName = $"{SanitizarNombreArchivo(nombreEmpleado)}_{req.CodigoCpi}.jpg";

                    // 2.3 Decodificar base64 y guardar en wwwroot/uploads/fotos/
                    var base64 = req.FotoBase64!;
                    var commaIdx = base64.IndexOf(',');
                    if (commaIdx > 0) base64 = base64[(commaIdx + 1)..];

                    var bytes = Convert.FromBase64String(base64);

                    var webRoot = _env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
                    var folder = Path.Combine(webRoot, "uploads", "fotos");
                    Directory.CreateDirectory(folder);

                    var fullPath = Path.Combine(folder, fileSafeName);
                    await System.IO.File.WriteAllBytesAsync(fullPath, bytes);

                    // 2.4 Path relativo para servir estático
                    fotoPathRel = $"/uploads/fotos/{fileSafeName}";

                    // 2.5 Actualizar i_cab_prev_inve.pdf_normal_cpi con el path
                    const string SQL_UPD_PDF = @"
                        UPDATE 
                            dbo.i_cab_prev_inve
                        SET 
                            pdf_normal_cpi = '\\10.39.10.30\EPPs\wwwroot' + REPLACE(@ruta, '/', '\')
                        WHERE 
                            codigo_cpi = @codigo_cpi;";

                    await using (var cmdPdf = new SqlCommand(SQL_UPD_PDF, cn, (SqlTransaction)tx))
                    {
                        cmdPdf.Parameters.Add(new SqlParameter("@ruta", SqlDbType.NVarChar, 500) { Value = (object)fotoPathRel ?? DBNull.Value });
                        cmdPdf.Parameters.Add(new SqlParameter("@codigo_cpi", SqlDbType.NVarChar, 50) { Value = req.CodigoCpi! });
                        await cmdPdf.ExecuteNonQueryAsync();
                    }
                }

                await tx.CommitAsync();

                // Guardar foto (si llegó)
                try
                {
                    if (!string.IsNullOrWhiteSpace(req.FotoBase64))
                    {
                        // req.FotoBase64 puede venir como "data:image/jpeg;base64,AAAA..."
                        var base64 = req.FotoBase64!;
                        var commaIdx = base64.IndexOf(',');
                        if (commaIdx > 0) base64 = base64[(commaIdx + 1)..];

                        var bytes = Convert.FromBase64String(base64);
                        var folder = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", "fotos");
                        Directory.CreateDirectory(folder);

                        var fileName = $"foto_{(req.CodigoCpi ?? "NA")}_{DateTime.UtcNow:yyyyMMddHHmmss}.jpg";
                        var fullPath = Path.Combine(folder, fileName);
                        await System.IO.File.WriteAllBytesAsync(fullPath, bytes);

                        fotoPathRel = $"/uploads/fotos/{fileName}";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error guardando foto");
                }

                // >>> Un único return con el resumen (el front muestra un solo alert)
                return Ok(new
                {
                    updated,
                    inserted,
                    deleted,
                    headersUpdated,
                    estado = estadoEpi,
                    foto = fotoPathRel
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                _logger.LogError(ex, "Error en GuardarPrevioDetalle");
                return StatusCode(500, "Error al guardar los cambios.");
            }
        }


        [HttpGet]
        public async Task<IActionResult> HistorialArticulo(string codigoEmp, string q, string? exclude)
        {
            if (string.IsNullOrWhiteSpace(codigoEmp) || string.IsNullOrWhiteSpace(q))
                return Json(Array.Empty<object>());

            var cs = _config.GetConnectionString(_connection);
            // Filtrar por los últimos 12 meses y artículos cuyo nombre empiece por "q"
            const string sql = @"
                SELECT
                    i_cab_prev_inve.fecha_elabo_cpi,
                    a.nombre_art,
                    d.cantidad_dpv
                FROM 
                    dbo.i_det_prev_inve AS d
                    INNER JOIN dbo.i_cab_prev_inve ON dbo.i_cab_prev_inve.codigo_cpi = d.codigo_cpi
                    INNER JOIN dbo.c_articulo AS a ON a.codigo_art = d.codigo_art 
                    --INNER JOIN dbo.i_det_comp_inve as i ON i.codigo_dpv = d.codigo_dpv -- Solo articulos del previo entregados 
                WHERE dbo.i_cab_prev_inve.codigo_epi = @codigo_epi
                    --AND dbo.i_cab_prev_inve.observacion_cpi LIKE 'EPP%'
                    AND dbo.i_cab_prev_inve.codigo_tti = @codigo_tti
                    AND dbo.i_cab_prev_inve.codigo_emp LIKE '00' + RIGHT(@codigo_emp,LEN(@codigo_emp)-2)
                    AND a.nombre_art LIKE @pat
                    AND dbo.i_cab_prev_inve.fecha_elabo_cpi >= DATEADD(MONTH, -12, GETDATE())
                    AND d.codigo_cpi NOT IN (SELECT codigo_cpi from dbo.i_det_prev_inve WHERE codigo_dpv = @exclude)
                ORDER BY 
                    dbo.i_cab_prev_inve.fecha_elabo_cpi DESC;";

            var list = new List<object>();

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add(new SqlParameter("@codigo_emp", SqlDbType.NVarChar, 50) { Value = codigoEmp });
            cmd.Parameters.Add(new SqlParameter("@pat", SqlDbType.NVarChar, 200) { Value = q + "%" });
            cmd.Parameters.Add(new SqlParameter("@codigo_epi", SqlDbType.NVarChar, 200) { Value = (object?)_codigo_epi_aprobado ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@codigo_tti", SqlDbType.NVarChar, 200) { Value = (object?)_codigo_tti_consumo ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@exclude", SqlDbType.NVarChar, 50) { Value = (object?)exclude ?? DBNull.Value });

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var fecha = rdr.GetDateTime(0).ToString("yyyy-MM-dd");
                var nombre = rdr.GetString(1);
                var cantidad = rdr.GetDecimal(2);
                list.Add(new { fecha, nombre, cantidad });
            }

            return Json(list);
        }

        private void SetVarsEmpresaFromCookie()
        {
            var emp = Request?.Cookies["empresa"] ?? "";

            // Mapea SIEMPRE a una lista blanca (nunca uses el cookie directo en SQL)
            switch (emp)
            {
                case "Bellarosa":
                    _connection = "bellarosaConnection";
                    _codigo_nef = "00102";          //Nivel del centro de costo
                    _codigo_epi_aprobado = "00103"; //Estado previo inventario
                    _codigo_epi_anulado = "00105";  //Estado previo inventario
                    _codigo_usu_aprueba = "017";  //Usuario MPAGUAY
                    _codigo_tti_consumo = "001005";  //Tipo de comprobante de inventario
                    break;
                case "Qualisa":
                    _connection = "qualisaConnection";
                    _codigo_nef = "00102";          //Nivel del centro de costo
                    _codigo_epi_aprobado = "00103"; //Estado previo inventario
                    _codigo_epi_anulado = "00105";  //Estado previo inventario
                    _codigo_usu_aprueba = "138";  //Usuario MPAGUAY
                    _codigo_tti_consumo = "001012";  //Tipo de comprobante de inventario
                    break;
                case "Royal Flowers":
                    _connection = "royalFlowersConnection";
                    _codigo_nef = "00102";          //Nivel del centro de costo
                    _codigo_epi_aprobado = "00103"; //Estado previo inventario
                    _codigo_epi_anulado = "00104";  //Estado previo inventario
                    _codigo_usu_aprueba = "465";  //Usuario MPAGUAY
                    _codigo_tti_consumo = "001012";  //Tipo de comprobante de inventario
                    break;
                case "Sisapamba":
                    _connection = "sisapambaConnection";
                    _codigo_nef = "00102";          //Nivel del centro de costo
                    _codigo_epi_aprobado = "00103"; //Estado previo inventario
                    _codigo_epi_anulado = "00105";  //Estado previo inventario
                    _codigo_usu_aprueba = "004";  //Usuario MPAGUAY
                    _codigo_tti_consumo = "001029";  //Tipo de comprobante de inventario
                    break;
                case "Continental Logistics":
                    _connection = "continentalLogisticsConnection";
                    _codigo_nef = "00102";          //Nivel del centro de costo
                    _codigo_epi_aprobado = "00103"; //Estado previo inventario
                    _codigo_epi_anulado = "00105";  //Estado previo inventario
                    _codigo_usu_aprueba = "026";  //Usuario MPAGUAY
                    _codigo_tti_consumo = "001005";  //Tipo de comprobante de inventario
                    break;
                default:
                    _connection = "defaultConnection";
                    _codigo_nef = "00102";          //Nivel del centro de costo
                    _codigo_epi_aprobado = "00103"; //Estado previo inventario
                    _codigo_epi_anulado = "00105";  //Estado previo inventario
                    _codigo_usu_aprueba = "004";  //Usuario MPAGUAY
                    _codigo_tti_consumo = "001029";  //Tipo de comprobante de inventario
                    break;
            }
        }

        [HttpGet]
        public async Task<IActionResult> ReporteInventario(string? empresa, string? codigo_emp, DateTime? desde, DateTime? hasta)
        {
            // SIN COOKIES: la empresa llega por querystring
            var vm = new ReporteFiltroVM
            {
                Empresa = empresa,
                CodigoEmp = codigo_emp,
                Desde = desde,
                Hasta = hasta
            };

            // Si no hay filtros, solo muestra la vista vacía
            if (string.IsNullOrWhiteSpace(empresa) && string.IsNullOrWhiteSpace(codigo_emp) && !desde.HasValue && !hasta.HasValue)
                return View(vm);

            // Si tu app usa conexiones por empresa, haz un switch/allow-list AQUI (sin cookies)
            var connName = "DefaultConnection";
            switch (empresa)
            {
                case "Bellarosa":
                    connName = "bellarosaConnection";
                    break;
                case "Qualisa":
                    connName = "qualisaConnection";
                    break;
                case "Royal Flowers":
                    connName = "royalFlowersConnection";
                    break;
                case "Sisapamba":
                    connName = "sisapambaConnection";
                    break;
                case "Continental Logistics":
                    connName = "continentalLogisticsConnection";
                    break;
                default:
                    connName = "defaultConnection";
                    break;
            }

            var cs = _config.GetConnectionString(connName);
            const string sql = @"
                SELECT 
                    i_cab_prev_inve.codigo_cpi,
                    i_cab_prev_inve.fecha_elabo_cpi,
                    a.nombre_art,
                    d.cantidad_dpv,
                    ISNULL(REPLACE(dbo.i_cab_prev_inve.pdf_normal_cpi,'\\10.39.10.30\EPPs\wwwroot\uploads\fotos\','http://10.39.10.30:3777/uploads/fotos/'),'') AS foto
                FROM dbo.i_cab_prev_inve
                INNER JOIN dbo.i_det_prev_inve d ON d.codigo_cpi = dbo.i_cab_prev_inve.codigo_cpi
                INNER JOIN dbo.c_articulo a ON a.codigo_art = d.codigo_art
                --INNER JOIN dbo.i_det_comp_inve e ON e.codigo_dpv = d.codigo_dpv -- Solo entregados
                WHERE --(@empresa = '' OR @empresa IS NULL OR dbo.i_cab_prev_inve.empresa = @empresa)
                  --AND 
                  (@codigo_emp = '' OR @codigo_emp IS NULL OR dbo.i_cab_prev_inve.codigo_emp LIKE '00' + RIGHT(@codigo_emp,LEN(@codigo_emp)-2)) AND
                  (((@desde IS NULL OR dbo.i_cab_prev_inve.fecha_elabo_cpi >= @desde)
                  AND (@hasta IS NULL OR dbo.i_cab_prev_inve.fecha_elabo_cpi < DATEADD(DAY,1,@hasta))) OR 
                  ((@desde IS NULL OR dbo.i_cab_prev_inve.fecha_efect_cpi >= @desde)
                  AND (@hasta IS NULL OR dbo.i_cab_prev_inve.fecha_efect_cpi < DATEADD(DAY,1,@hasta)))) 
                  AND dbo.i_cab_prev_inve.CODIGO_EPI = @codigo_epi
                ORDER BY dbo.i_cab_prev_inve.codigo_cpi, dbo.i_cab_prev_inve.fecha_elabo_cpi, a.nombre_art;";

            var grupos = new Dictionary<string, ReporteGrupoVM>();

            await using var cn = new SqlConnection(cs);
            await cn.OpenAsync();
            await using var cmd = new SqlCommand(sql, cn);
            cmd.Parameters.Add(new SqlParameter("@empresa", SqlDbType.NVarChar, 100) { Value = (object?)empresa ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@codigo_emp", SqlDbType.NVarChar, 50) { Value = (object?)codigo_emp ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@codigo_epi", SqlDbType.NVarChar, 50) { Value = (object?)_codigo_epi_aprobado ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@desde", SqlDbType.DateTime) { Value = (object?)desde ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@hasta", SqlDbType.DateTime) { Value = (object?)hasta ?? DBNull.Value });

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var cpi = rdr.GetString(0);
                var fec = rdr.GetDateTime(1);
                var art = rdr.GetString(2);
                var cant = rdr.GetDecimal(3);
                var foto = rdr.IsDBNull(4) ? "" : rdr.GetString(4);

                if (!grupos.TryGetValue(cpi, out var g))
                {
                    g = new ReporteGrupoVM { CodigoCpi = cpi, FotoUrl = string.IsNullOrWhiteSpace(foto) ? null : foto };
                    grupos[cpi] = g;
                }
                g.Lineas.Add(new ReporteLineaVM
                {
                    FechaElaboCpi = fec,
                    Articulo = art,
                    Cantidad = cant
                });
            }

            /* --- consulta del nombre: OTRA CONEXIÓN --- */
            ViewBag.NombreEmpleado = "";
            if (!string.IsNullOrWhiteSpace(codigo_emp))
            {
                await using var cn2 = new SqlConnection(cs); // distinta conexión
                await cn2.OpenAsync();
                const string sqlEmp = @"
                   SELECT 
	                    apellido_emp + ' ' + nombre_emp + ' | ' + nombre_eor + ' | ' + nombre_cag
                    FROM 
	                    dbo.r_empleado INNER JOIN
	                    dbo.r_estruc_organi ON dbo.r_estruc_organi.codigo_eor = dbo.r_empleado.codigo_eor INNER JOIN
	                    dbo.r_cargo on dbo.r_cargo.codigo_cag = dbo.r_empleado.codigo_cag
                    WHERE
                        dbo.r_empleado.codigo_emp LIKE '00' + RIGHT(@codigo_emp,LEN(@codigo_emp)-2);";
                await using var cmdEmp = new SqlCommand(sqlEmp, cn2);
                cmdEmp.Parameters.Add(new SqlParameter("@codigo_emp", SqlDbType.NVarChar, 50) { Value = codigo_emp });
                var result = await cmdEmp.ExecuteScalarAsync();
                ViewBag.NombreEmpleado = result?.ToString() ?? "";
            }
            else
            {
                ViewBag.NombreEmpleado = "Empleado no encontrado...";
            }

            vm.Grupos = grupos.Values.ToList();
            return View(vm);
        }

    }
}



