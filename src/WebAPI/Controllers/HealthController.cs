using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProTraining.Application.Common;

namespace ProTraining.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IAppDbContext _db;

    public HealthController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> CheckHealth()
    {
        try
        {
            // Verificación activa de conexión a PostgreSQL
            bool canConnect = await _db.Accounts.AnyAsync();
            return Ok(new
            {
                status = "Healthy",
                service = "ProTraining.API",
                database = canConnect ? "Connected" : "Reachable",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                status = "Unhealthy",
                service = "ProTraining.API",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }
}
