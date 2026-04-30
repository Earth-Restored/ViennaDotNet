using Microsoft.EntityFrameworkCore;
using Solace.ApiServer.Models;

namespace Solace.ApiServer;

public class LiveDbContext : DbContext
{
    public LiveDbContext(DbContextOptions<LiveDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts { get; set; }
}
