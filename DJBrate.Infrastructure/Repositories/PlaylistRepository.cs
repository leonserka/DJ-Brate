using DJBrate.Domain.Entities;
using DJBrate.Domain.Interfaces;
using DJBrate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DJBrate.Infrastructure.Repositories;

public class PlaylistRepository : Repository<Playlist>, IPlaylistRepository
{
    public PlaylistRepository(AppDbContext context) : base(context) { }

    public async Task<IEnumerable<Playlist>> GetByUserIdAsync(Guid userId)
        => await _dbSet
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    public async Task<Playlist?> GetByIdWithTracksAsync(Guid id)
        => await _dbSet
            .Include(p => p.PlaylistTracks)
            .FirstOrDefaultAsync(p => p.Id == id);
}
