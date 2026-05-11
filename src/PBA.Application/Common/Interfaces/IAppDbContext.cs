using Microsoft.EntityFrameworkCore;
using PBA.Domain.Entities;

namespace PBA.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Idea> Ideas { get; }
    DbSet<SavedIdea> SavedIdeas { get; }
    DbSet<IdeaSource> IdeaSources { get; }
}
