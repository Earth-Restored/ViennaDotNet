using Microsoft.EntityFrameworkCore;
using Solace.DB.Models;
using Solace.DB.Models.Global;
using Solace.DB.Models.Player;
using Solace.DB.Models.Player.Workshop;

namespace Solace.DB;

public sealed class EarthDbContext : DbContext
{
    public EarthDbContext(DbContextOptions<EarthDbContext> options)
        : base(options)
    {
    }

    public DbSet<Account> Accounts { get; set; }

    public DbSet<ProfileEF> Profiles { get; set; }

    public DbSet<ActivityLogEF> ActivityLogs { get; set; }

    public DbSet<BoostsEF> Boosts { get; set; }

    public DbSet<BuildplateEF> PlayerBuildplates { get; set; }

    public DbSet<HotbarEF> Hotbars { get; set; }

    public DbSet<InventoryEF> Inventories { get; set; }

    public DbSet<JournalEF> Journals { get; set; }

    public DbSet<RedeemedTappablesEF> RedeemedTappables { get; set; }

    public DbSet<TokensEF> Tokens { get; set; }

    public DbSet<CraftingSlotsEF> CraftingSlots { get; set; }

    public DbSet<SmeltingSlotsEF> SmeltingSlots { get; set; }

    public DbSet<EncounterBuildplateEF> EncounterBuildplates { get; set; }

    public DbSet<SharedBuildplateEF> SharedBuildplates { get; set; }

    public DbSet<TemplateBuildplateEF> TemplateBuildplates { get; set; }
    
    public DbSet<Tile> Tiles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IVersionedEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IVersionedEntity.Version))
                    .IsConcurrencyToken();
            }
        }

        // account
        modelBuilder.Entity<Account>()
            .HasOne(a => a.Profile)
            .WithOne(p => p.Account)
            .HasForeignKey<ProfileEF>(p => p.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasOne(a => a.ActivityLog)
            .WithOne(a => a.Account)
            .HasForeignKey<ActivityLogEF>(a => a.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasOne(a => a.Boosts)
            .WithOne(b => b.Account)
            .HasForeignKey<BoostsEF>(a => a.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasMany(a => a.Buildplates)
            .WithOne(b => b.Account)
            .HasForeignKey(b => b.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasOne(a => a.Hotbar)
            .WithOne(h => h.Account)
            .HasForeignKey<HotbarEF>(h => h.Id)
            .OnDelete(DeleteBehavior.Cascade);
            
        modelBuilder.Entity<Account>()
            .HasOne(a => a.Inventory)
            .WithOne(i => i.Account)
            .HasForeignKey<InventoryEF>(i => i.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasOne(a => a.Journal)
            .WithOne(j => j.Account)
            .HasForeignKey<JournalEF>(j => j.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasOne(a => a.RedeemedTappables)
            .WithOne(r => r.Account)
            .HasForeignKey<RedeemedTappablesEF>(r => r.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasOne(a => a.Tokens)
            .WithOne(t => t.Account)
            .HasForeignKey<TokensEF>(t => t.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasOne(a => a.CraftingSlots)
            .WithOne(c => c.Account)
            .HasForeignKey<CraftingSlotsEF>(c => c.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasOne(a => a.SmeltingSlots)
            .WithOne(s => s.Account)
            .HasForeignKey<SmeltingSlotsEF>(s => s.Id)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Account>()
            .HasMany(a => a.SharedBuildplates)
            .WithOne(s => s.Account)
            .HasForeignKey(s => s.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // profile
        modelBuilder.Entity<ProfileEF>()
            .OwnsOne(x => x.Rubies, builder => builder.ToJson());

        // activity log
        modelBuilder.Entity<ActivityLogEF>()
            .OwnsOne(x => x.Entries, builder => builder.ToJson());

        // boosts
        modelBuilder.Entity<BoostsEF>()
            .OwnsOne(x => x.ActiveBoosts, builder => builder.ToJson());

        // hotbar
        modelBuilder.Entity<HotbarEF>()
            .OwnsOne(x => x.Items, builder => builder.ToJson());

        // inventory
        modelBuilder.Entity<InventoryEF>()
            .OwnsOne(x => x.StackableItemsData, builder => builder.ToJson())
            .OwnsOne(x => x.NonStackableItemsData, builder => builder.ToJson());

        // journal
        modelBuilder.Entity<JournalEF>()
            .OwnsOne(x => x.Items, builder => builder.ToJson());

        // redeemed tappables
        modelBuilder.Entity<RedeemedTappablesEF>()
            .OwnsOne(x => x.Tappables, builder => builder.ToJson());

        // tokens
        modelBuilder.Entity<TokensEF>()
            .OwnsOne(x => x.Tokens, builder => builder.ToJson());

        // crafting slots
        modelBuilder.Entity<CraftingSlotsEF>()
            .OwnsOne(x => x.Slots, builder => builder.ToJson());

        // smelting slots
        modelBuilder.Entity<SmeltingSlotsEF>()
            .OwnsOne(x => x.Slots, builder => builder.ToJson());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries<IVersionedEntity>();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.Version = 1;
                    break;
                case EntityState.Modified:
                    entry.Entity.Version++;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}