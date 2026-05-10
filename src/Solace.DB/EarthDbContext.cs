using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Solace.DB.Models;
using Solace.DB.Models.Common;
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
            .Property(x => x.Entries)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                v => JsonSerializer.Deserialize<List<ActivityLogEF.Entry>>(v, (JsonSerializerOptions)null!) 
                    ?? new List<ActivityLogEF.Entry>()
            )
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new ListValueComparer<ActivityLogEF.Entry>());

        // boosts
        modelBuilder.Entity<BoostsEF>()
            .OwnsMany(x => x.ActiveBoosts, builder => builder.ToJson());

        // hotbar
        modelBuilder.Entity<HotbarEF>()
            .OwnsMany(x => x.Items, builder => builder.ToJson());

        // inventory
        modelBuilder.Ignore<NonStackableItemInstance>();

        modelBuilder.Entity<InventoryEF>()
            .Property(x => x.StackableItemsData)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                v => JsonSerializer.Deserialize<Dictionary<string, int?>>(v, (JsonSerializerOptions)null!) 
                    ?? new Dictionary<string, int?>()
            )
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new DictionaryValueComparer<string, int?>());

        modelBuilder.Entity<InventoryEF>()
            .Property(x => x.NonStackableItemsData)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                v => JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, NonStackableItemInstance>>>(v, (JsonSerializerOptions)null!) 
                    ?? new Dictionary<string, Dictionary<string, NonStackableItemInstance>>()
            )
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new NestedDictionaryValueComparer());

        // journal
        modelBuilder.Entity<JournalEF>()
            .Property(x => x.Items)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                v => JsonSerializer.Deserialize<Dictionary<string, JournalEF.ItemJournalEntry>>(v, (JsonSerializerOptions)null!) 
                    ?? new Dictionary<string, JournalEF.ItemJournalEntry>()
            )
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new DictionaryValueComparer<string, JournalEF.ItemJournalEntry>());

        // redeemed tappables
        modelBuilder.Entity<RedeemedTappablesEF>()
            .OwnsOne(x => x.Tappables, builder => builder.ToJson());

        // tokens
        modelBuilder.Entity<TokensEF>()
            .Property(x => x.Tokens)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null!),
                v => JsonSerializer.Deserialize<Dictionary<string, TokensEF.Token>>(v, (JsonSerializerOptions)null!) 
                    ?? new Dictionary<string, TokensEF.Token>()
            )
            .HasColumnType("nvarchar(max)")
            .Metadata.SetValueComparer(new DictionaryValueComparer<string, TokensEF.Token>());

        // crafting slots
        modelBuilder.Ignore<CraftingSlotEF.ActiveJobR>();

        modelBuilder.Entity<CraftingSlotsEF>()
            .OwnsMany(x => x.Slots, builder => builder.ToJson());

        // smelting slots
        modelBuilder.Ignore<SmeltingSlot.ActiveJobR>();
        modelBuilder.Ignore<SmeltingSlot.BurningR>();
        modelBuilder.Ignore<SmeltingSlot.Fuel >();

        modelBuilder.Entity<SmeltingSlotsEF>()
            .OwnsMany(x => x.Slots, builder => builder.ToJson());

        // shared buildplates
        modelBuilder.Entity<SharedBuildplateEF>()
            .OwnsMany(x => x.Hotbar, builder => builder.ToJson());
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

public class ListValueComparer<T> : ValueComparer<List<T>>
    where T : IEquatable<T>
{
    public ListValueComparer()
        : base(
            (c1, c2) => c1 == c2 || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
            c => c != null ? c.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode())) : 0,
            c => new List<T>(c))
    {
    }
}

public class DictionaryValueComparer<TKey, TValue> : ValueComparer<Dictionary<TKey, TValue>>
    where TKey : notnull
{
    public DictionaryValueComparer()
        : base(
            (d1, d2) => DictionariesEqual(d1, d2),
            d => ComputeHashCode(d),
            d => new Dictionary<TKey, TValue>(d))
    {
    }

    private static bool DictionariesEqual(Dictionary<TKey, TValue>? d1, Dictionary<TKey, TValue>? d2)
    {
        if (d1 == d2)
        {
            return true;
        }

        if (d1 == null || d2 == null)
        {
            return false;
        }

        if (d1.Count != d2.Count)
        {
            return false;
        }

        foreach (var kvp in d1)
        {
            if (!d2.TryGetValue(kvp.Key, out var value2))
            {
                return false;
            }

            if (!EqualityComparer<TValue>.Default.Equals(kvp.Value, value2))
            {
                return false;
            }
        }

        return true;
    }

    private static int ComputeHashCode(Dictionary<TKey, TValue>? d)
    {
        if (d == null)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var kvp in d.OrderBy(x => x.Key))
        {
            hash.Add(kvp.Key);
            hash.Add(kvp.Value);
        }

        return hash.ToHashCode();
    }
}

public class NestedDictionaryValueComparer : ValueComparer<Dictionary<string, Dictionary<string, NonStackableItemInstance>>>
{
    public NestedDictionaryValueComparer()
        : base(
            (d1, d2) => OuterDictionariesEqual(d1, d2),
            d => ComputeOuterHashCode(d),
            d => d.ToDictionary(x => x.Key, x => new Dictionary<string, NonStackableItemInstance>(x.Value)))
    {
    }

    private static bool OuterDictionariesEqual(Dictionary<string, Dictionary<string, NonStackableItemInstance>>? d1, Dictionary<string, Dictionary<string, NonStackableItemInstance>>? d2)
    {
        if (d1 == d2)
        {
            return true;
        }

        if (d1 == null || d2 == null)
        {
            return false;
        }

        if (d1.Count != d2.Count)
        {
            return false;
        }

        foreach (var kvp in d1)
        {
            if (!d2.TryGetValue(kvp.Key, out var innerDict2))
            {
                return false;
            }

            if (!InnerDictionariesEqual(kvp.Value, innerDict2))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InnerDictionariesEqual(Dictionary<string, NonStackableItemInstance>? d1, Dictionary<string, NonStackableItemInstance>? d2)
    {
        if (d1 == d2)
        {
            return true;
        }

        if (d1 == null || d2 == null)
        {
            return false;
        }

        if (d1.Count != d2.Count)
        {
            return false;
        }

        foreach (var kvp in d1)
        {
            if (!d2.TryGetValue(kvp.Key, out var item2))
            {
                return false;
            }

            if (!kvp.Value.Equals(item2))
            {
                return false;
            }
        }

        return true;
    }

    private static int ComputeOuterHashCode(Dictionary<string, Dictionary<string, NonStackableItemInstance>>? d)
    {
        if (d == null)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var kvp in d.OrderBy(x => x.Key))
        {
            hash.Add(kvp.Key);
            foreach (var innerKvp in kvp.Value.OrderBy(x => x.Key))
            {
                hash.Add(innerKvp.Key);
                hash.Add(innerKvp.Value);
            }
        }

        return hash.ToHashCode();
    }
}