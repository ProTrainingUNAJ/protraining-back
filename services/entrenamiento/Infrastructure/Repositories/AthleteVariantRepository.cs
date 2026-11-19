using Application.Interfaces.Persistence;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class AthleteVariantRepository : IAthleteVariantRepository
    {
        private readonly AppDbContext _context;

        public AthleteVariantRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<AthleteVariant>> GetActiveApprovedByAthleteAsync(Guid athleteId, DateOnly date, CancellationToken cancellationToken = default)
        {
            return await _context.AthleteVariants.AsNoTracking().Where(v => v.AthleteId == athleteId
                                                                        && v.IsApproved
                                                                        && v.IsActive
                                                                        && v.ValidFrom <= date
                                                                        && (v.ValidTo == null || v.ValidTo >= date))
                                                                        .OrderBy(v => v.ValidFrom)
                                                                        .ThenBy(v => v.CreatedAt)
                                                                        .ToListAsync(cancellationToken);
        }
    }
}
