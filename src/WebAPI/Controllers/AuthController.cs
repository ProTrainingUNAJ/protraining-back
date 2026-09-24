using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProTraining.Application.Common;
using ProTraining.Application.DTOs;
using ProTraining.Common;
using ProTraining.Domain.Entities;
using ProTraining.Infrastructure.Auth;

namespace ProTraining.WebAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly JwtService _jwtService;

    public AuthController(IAppDbContext db, JwtService jwtService)
    {
        _db = db;
        _jwtService = jwtService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var account = await _db.Accounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Email.ToLower() == request.Email.ToLower().Trim());

        if (account == null || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
        {
            throw new DomainException("Credenciales inválidas. Compruebe el email y contraseña ingresados.");
        }

        Guid? athleteId = null;
        if (account.Role == UserRole.Athlete)
        {
            var athlete = await _db.Athletes
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.UserId == account.Id || a.Email.ToLower() == account.Email.ToLower());
            athleteId = athlete?.Id;
        }

        var token = _jwtService.GenerateToken(account, athleteId);

        return Ok(new LoginResponse(
            Token: token,
            FullName: account.FullName,
            Email: account.Email,
            Role: account.Role.ToString(),
            AccountId: account.Id,
            AthleteId: athleteId
        ));
    }

    [HttpPost("register")]
    public async Task<ActionResult<LoginResponse>> Register([FromBody] RegisterRequest request)
    {
        bool exists = await _db.Accounts.IgnoreQueryFilters().AnyAsync(a => a.Email.ToLower() == request.Email.ToLower().Trim());
        if (exists)
        {
            throw new DomainException("Ya existe una cuenta registrada con este correo electrónico.");
        }

        var account = new Account
        {
            FullName = request.FullName.Trim(),
            Email = request.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.PF
        };

        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();

        var token = _jwtService.GenerateToken(account);

        return Ok(new LoginResponse(
            Token: token,
            FullName: account.FullName,
            Email: account.Email,
            Role: account.Role.ToString(),
            AccountId: account.Id,
            AthleteId: null
        ));
    }
}
