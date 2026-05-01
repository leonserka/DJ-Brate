using DJBrate.Domain.Entities;
using DJBrate.Domain.Interfaces;
using DJBrate.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DJBrate.Infrastructure.Repositories;

public class AiConversationMessageRepository : Repository<AiConversationMessage>, IAiConversationMessageRepository
{
    public AiConversationMessageRepository(AppDbContext context) : base(context) { }

    public async Task<List<AiConversationMessage>> GetBySessionIdAsync(Guid sessionId)
        => await _dbSet
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.SequenceOrder)
            .ToListAsync();
}
