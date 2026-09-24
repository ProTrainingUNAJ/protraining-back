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
public class OrganizationsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public OrganizationsController(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<List<OrganizationDto>>> GetOrganizations()
    {
        var list = await _db.Organizations
            .Include(o => o.Categories)
            .OrderBy(o => o.Name)
            .Select(o => new OrganizationDto(
                o.Id,
                o.Name,
                o.Description,
                o.Categories.Count
            ))
            .ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationDto>> CreateOrganization([FromBody] CreateOrganizationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("El nombre de la organización es obligatorio.");

        if (!_currentUser.AccountId.HasValue)
            throw new DomainException("No se pudo identificar la cuenta del preparador físico.");

        var org = new Organization
        {
            AccountId = _currentUser.AccountId.Value,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim()
        };

        _db.Organizations.Add(org);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetOrganizations), new { id = org.Id },
            new OrganizationDto(org.Id, org.Name, org.Description, 0));
    }

    [HttpGet("{organizationId}/categories")]
    public async Task<ActionResult<List<CategoryDto>>> GetCategories(Guid organizationId)
    {
        var categories = await _db.Categories
            .Where(c => c.OrganizationId == organizationId)
            .Include(c => c.AthleteCategories)
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(
                c.Id,
                c.OrganizationId,
                c.Name,
                c.AthleteCategories.Count
            ))
            .ToListAsync();

        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<ActionResult<CategoryDto>> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("El nombre de la categoría es obligatorio.");

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == request.OrganizationId);
        if (org == null)
            throw new NotFoundException("Organización no encontrada.");

        var category = new Category
        {
            AccountId = org.AccountId,
            OrganizationId = org.Id,
            Name = request.Name.Trim()
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return Ok(new CategoryDto(category.Id, category.OrganizationId, category.Name, 0));
    }
}
