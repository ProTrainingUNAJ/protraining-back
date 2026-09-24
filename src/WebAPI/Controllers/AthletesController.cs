using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProTraining.Application.Common;
using ProTraining.Application.DTOs;
using ProTraining.Common;
using ProTraining.Domain.Entities;

namespace ProTraining.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AthletesController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public AthletesController(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<AthleteDto>>> GetAthletes()
    {
        var athletes = await _db.Athletes
            .Include(a => a.AthleteCategories)
                .ThenInclude(ac => ac.Category)
            .Include(a => a.Injuries)
            .OrderBy(a => a.LastName)
            .ThenBy(a => a.FirstName)
            .Select(a => new AthleteDto(
                a.Id,
                a.FirstName,
                a.LastName,
                a.Email,
                a.Phone,
                a.MedicalConditions,
                a.EmergencyContact,
                a.AthleteCategories.Select(ac => ac.Category!.Name).ToList(),
                a.Injuries.Count(i => i.Status == InjuryStatus.Active)
            ))
            .ToListAsync();

        return Ok(athletes);
    }

    [HttpPost]
    public async Task<ActionResult<AthleteDto>> CreateAthlete([FromBody] CreateAthleteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            throw new DomainException("El nombre y apellido del atleta son obligatorios.");

        if (!_currentUser.AccountId.HasValue)
            throw new DomainException("No se pudo identificar la cuenta del preparador físico.");

        var accountId = _currentUser.AccountId.Value;

        var athlete = new Athlete
        {
            AccountId = accountId,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone.Trim(),
            MedicalConditions = request.MedicalConditions?.Trim(),
            EmergencyContact = request.EmergencyContact?.Trim(),
            ConsentGiven = true
        };

        if (request.CategoryIds != null && request.CategoryIds.Count > 0)
        {
            foreach (var catId in request.CategoryIds.Distinct())
            {
                athlete.AthleteCategories.Add(new AthleteCategory
                {
                    AthleteId = athlete.Id,
                    CategoryId = catId,
                    AccountId = accountId
                });
            }
        }

        _db.Athletes.Add(athlete);
        await _db.SaveChangesAsync();

        return Ok(new AthleteDto(
            athlete.Id,
            athlete.FirstName,
            athlete.LastName,
            athlete.Email,
            athlete.Phone,
            athlete.MedicalConditions,
            athlete.EmergencyContact,
            new List<string>(),
            0
        ));
    }

    [HttpPost("{athleteId}/categories/{categoryId}")]
    public async Task<IActionResult> AssignToCategory(Guid athleteId, Guid categoryId)
    {
        var athlete = await _db.Athletes
            .Include(a => a.AthleteCategories)
            .FirstOrDefaultAsync(a => a.Id == athleteId);

        if (athlete == null)
            throw new NotFoundException("Atleta no encontrado.");

        if (athlete.AthleteCategories.Any(ac => ac.CategoryId == categoryId))
        {
            // Según CU-02: "Si el atleta ya está en esa categoría, el sistema lo indica y no duplica."
            return Ok(new { message = "El atleta ya se encuentra asignado a esta categoría." });
        }

        athlete.AthleteCategories.Add(new AthleteCategory
        {
            AthleteId = athleteId,
            CategoryId = categoryId,
            AccountId = athlete.AccountId
        });

        await _db.SaveChangesAsync();
        return Ok(new { message = "Atleta asignado exitosamente a la categoría." });
    }
}
