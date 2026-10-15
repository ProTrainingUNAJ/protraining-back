using Application.DTOs.Common;
using Application.UseCases.Health.GetHealth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/health")]
public sealed class HealthController(GetHealthQueryHandler healthHandler) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<HealthDto>> Get(CancellationToken cancellationToken)
