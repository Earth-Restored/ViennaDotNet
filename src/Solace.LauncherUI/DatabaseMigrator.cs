using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Solace.Common;
using Solace.DB;
using Solace.DB.Models.Common;
using Solace.DB.Models.Global;
using Solace.DB.Models.Player;
using Solace.DB.Models.Player.Workshop;

namespace Solace.LauncherUI;

internal sealed class DatabaseMigrator
{
    private static readonly JsonSerializerOptions legacyDbJsonOptions = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly EarthDbContext _earthDb;
    private readonly SqliteConnection _legacyEarthDb;
#pragma warning disable CS0618 // Type or member is obsolete - needed for migration
    private readonly LiveDbContext? _liveDb;
#pragma warning restore CS0618 // Type or member is obsolete

    private readonly Dictionary<string, Guid> _oldToNewId = [];

#pragma warning disable CS0618 // Type or member is obsolete - needed for migration
    public DatabaseMigrator(EarthDbContext earthDb, SqliteConnection legacyEarthDb, LiveDbContext? liveDb)
#pragma warning restore CS0618 // Type or member is obsolete
    {
        _earthDb = earthDb;
        _legacyEarthDb = legacyEarthDb;
        _liveDb = liveDb;
    }

    public async Task MigrateAsync()
    {
        _oldToNewId.Clear();

        int saveCounter = 0;

        // objects
        using (var command = _legacyEarthDb.CreateCommand())
        {
            command.CommandText = "SELECT type, id, value FROM objects";

            using (var reader = command.ExecuteReader())
            {
                while (await reader.ReadAsync())
                {
                    string type = reader.GetString(0);
                    string id = reader.GetString(1);
                    string value = reader.GetString(2);

                    try
                    {
                        await MigrateObject(type, id, value);

                        await SaveEarthChanges();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to migrate object '{type}', id: '{id}': {ex.Message}");
                    }
                }
            }
        }

        // tiles
        using (var command = _legacyEarthDb.CreateCommand())
        {
            command.CommandText = "SELECT id, value FROM tiles";

            using (var reader = command.ExecuteReader())
            {
                while (await reader.ReadAsync())
                {
                    ulong id = (ulong)reader.GetInt64(0);
                    string value = reader.GetString(1);

                    _earthDb.Tiles.Add(new Tile()
                    {
                        Id = id,
                        ObjectStoreId = value,
                    });

                    await SaveEarthChanges();
                }
            }
        }

        // template buildplates
        using (var command = _legacyEarthDb.CreateCommand())
        {
            command.CommandText = "SELECT id, value FROM tiles";

            using (var reader = command.ExecuteReader())
            {
                while (await reader.ReadAsync())
                {
                    string idString = reader.GetString(0);
                    string valueString = reader.GetString(1);

                    var template = ReadLegacyDbJson<TemplateBuildplateEF.Legacy>(valueString);

                    _earthDb.TemplateBuildplates.Add(new TemplateBuildplateEF()
                    {
                        Id = Guid.Parse(idString),
                        Name = template.Name,
                        Size = template.Size,
                        Offset = template.Offset,
                        Scale = template.Scale,
                        Night = template.Night,
                        ServerDataObjectId = template.ServerDataObjectId,
                        PreviewObjectId = template.PreviewObjectId,
                    });

                    await SaveEarthChanges();
                }
            }
        }

        await _earthDb.SaveChangesAsync();

        // live
        if (_liveDb is not null)
        {
            foreach (var oldAccount in _liveDb.Accounts)
            {
                var account = await _earthDb.GetOrCreateAccount(GetId(oldAccount.Id), query => query.AsNoTracking());

                account.CreatedDate = oldAccount.CreatedDate;
                account.Username = oldAccount.Username;
                account.ProfilePictureUrl = oldAccount.ProfilePictureUrl;
                account.FirstName = oldAccount.FirstName;
                account.LastName = oldAccount.LastName;
                account.PasswordSalt = oldAccount.PasswordSalt;
                account.PasswordHash = oldAccount.PasswordHash;
            }

            await _liveDb.SaveChangesAsync();
        }

        async Task SaveEarthChanges()
        {
            saveCounter++;

            if (saveCounter >= 100)
            {
                await _earthDb.SaveChangesAsync();
                saveCounter = 0;
            }
        }
    }

    private static T ReadLegacyDbJson<T>(string json)
    {
        var value = Json.Deserialize<T>(json, legacyDbJsonOptions);
        return value is null ? throw new InvalidDataException("Null json value.") : value;
    }

    private async Task MigrateObject(string type, string idString, string valueString)
    {
        switch (type)
        {
            case "profile":
                {
                    var value = ReadLegacyDbJson<ProfileEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var profile = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.Profile)))
                        .Profile!;

                    profile.Health = value.Health;
                    profile.Experience = value.Experience;
                    profile.Level = value.Level;
                    profile.Rubies = new(value.Rubies.Purchased, value.Rubies.Earned);
                }

                break;

            case "journal":
                {
                    var value = ReadLegacyDbJson<JournalEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var journal = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.Journal)))
                        .Journal!;

                    journal.Items = value._items.ToDictionary(item => item.Key, item => new JournalEF.ItemJournalEntry(item.Value.FirstSeen, item.Value.LastSeen, item.Value.AmountCollected));
                }

                break;
            case "hotbar":
                {
                    var value = ReadLegacyDbJson<HotbarEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var hotbar = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.Hotbar)))
                        .Hotbar!;

                    for (int i = 0; i < value.Items.Length; i++)
                    {
                        var item = value.Items[i];

                        hotbar.Items[i] = item is null ? null : new HotbarEF.Item(item.Uuid, item.Count, item.InstanceId);
                    }
                }

                break;
            case "inventory":
                {
                    var value = ReadLegacyDbJson<InventoryEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var inventory = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.Inventory)))
                        .Inventory!;

                    foreach (var (itemId, itemCount) in value.StackableItems)
                    {
                        inventory.StackableItemsData[itemId] = itemCount;
                    }

                    foreach (var (itemId, instances) in value.NonStackableItems)
                    {
                        inventory.NonStackableItemsData[itemId] = instances.ToDictionary(item => item.Key, item => new NonStackableItemInstance(item.Value.InstanceId, item.Value.Wear));
                    }
                }

                break;
            case "tokens":
                {
                    var value = ReadLegacyDbJson<TokensEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var tokens = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.Tokens)))
                        .Tokens!;

                    foreach (var (tokenId, token) in value.Tokens)
                    {
                        TokensEF.Token migratedToken = token switch
                        {
                            TokensEF.Legacy.LevelUpToken levelUp => new TokensEF.LevelUpToken(levelUp.Level, levelUp.Rewards),
                            TokensEF.Legacy.JournalItemUnlockedToken itemUnlocked => new TokensEF.JournalItemUnlockedToken(itemUnlocked.ItemId),
                            _ => throw new UnreachableException(),
                        };

                        tokens.Tokens[tokenId] = migratedToken;
                    }
                }

                break;
            case "crafting":
                {
                    var value = ReadLegacyDbJson<CraftingSlotsEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var craftingSlots = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.CraftingSlots)))
                        .CraftingSlots!;

                    for (int i = 0; i < value.Slots.Length; i++)
                    {
                        var slot = value.Slots[i];
                        var job = slot.ActiveJob;

                        craftingSlots.Slots[i] = new CraftingSlotEF()
                        {
                            ActiveJob = job is null
                                ? null
                                : new CraftingSlotEF.ActiveJobR(
                                    job.SessionId,
                                    job.RecipeId,
                                    job.StartTime,
                                    [.. job.Input.Select(item => new CraftingSlotEF.InputRow([.. item.Select(item => new InputItem(
                                        item.Id,
                                        item.Count,
                                        [.. item.Instances.Select(item => new NonStackableItemInstance(item.InstanceId, item.Wear))]))]))
                                    ],
                                    job.TotalRounds,
                                    job.CollectedRounds,
                                    job.FinishedEarly
                                ),
                            Locked = slot.Locked,
                        };
                    }
                }

                break;
            case "smelting":
                {
                    var value = ReadLegacyDbJson<SmeltingSlotsEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var smeltingSlots = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.SmeltingSlots)))
                        .SmeltingSlots!;

                    for (int i = 0; i < value.Slots.Length; i++)
                    {
                        var slot = value.Slots[i];
                        var job = slot.ActiveJob;
                        var burning = slot.Burning;

                        smeltingSlots.Slots[i] = new SmeltingSlot()
                        {
                            ActiveJob = job is null ? null : new SmeltingSlot.ActiveJobR(
                                job.SessionId,
                                job.RecipeId,
                                job.StartTime,
                                new(job.Input.Id, job.Input.Count, [.. job.Input.Instances.Select(item => new NonStackableItemInstance(item.InstanceId, item.Wear))]),
                                job.AddedFuel is null ? null : new(
                                    new(job.AddedFuel.Item.Id,
                                        job.AddedFuel.Item.Count,
                                        [.. job.AddedFuel.Item.Instances.Select(item => new NonStackableItemInstance(item.InstanceId, item.Wear))]
                                    ),
                                    job.AddedFuel.BurnDuration,
                                    job.AddedFuel.HeatPerSecond
                                ),
                                job.TotalRounds,
                                job.CollectedRounds,
                                job.FinishedEarly
                            ),
                            Locked = slot.Locked,
                        };
                    }
                }

                break;
            case "redeemedTappables":
                {
                    var value = ReadLegacyDbJson<RedeemedTappablesEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var redeemedTappables = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.RedeemedTappables)))
                        .RedeemedTappables!;

                    foreach (var item in value.Tappables)
                    {
                        if (Guid.TryParse(item.Key, out var tappableId))
                        {
                            redeemedTappables.Tappables[tappableId] = item.Value;
                        }
                        else
                        {
                            Log.Warning($"Failed to parse tappable id '{item.Key}' as UUID");
                        }
                    }
                }

                break;
            case "boosts":
                {
                    var value = ReadLegacyDbJson<BoostsEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var boosts = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.Boosts)))
                        .Boosts!;

                    for (int i = 0; i < value.ActiveBoosts.Length; i++)
                    {
                        var boost = value.ActiveBoosts[i];

                        boosts.ActiveBoosts[i] = boost is null
                            ? null
                            : new BoostsEF.ActiveBoost(boost.InstanceId, boost.ItemId, boost.StartTime, boost.Duration);
                    }
                }

                break;
            case "activityLog":
                {
                    var value = ReadLegacyDbJson<ActivityLogEF.Legacy>(valueString);

                    var id = GetId(idString);

                    var activityLog = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.ActivityLog)))
                        .ActivityLog!;

                    foreach (var entry in value.Entries)
                    {
                        activityLog.Entries.Add(entry switch
                        {
                            ActivityLogEF.Legacy.LevelUpEntry levelUp => new ActivityLogEF.LevelUpEntry(levelUp.Timestamp, levelUp.Level),
                            ActivityLogEF.Legacy.TappableEntry tappable => new ActivityLogEF.TappableEntry(tappable.Timestamp, tappable.Rewards),
                            ActivityLogEF.Legacy.JournalItemUnlockedEntry journalUnlock => new ActivityLogEF.JournalItemUnlockedEntry(journalUnlock.Timestamp, journalUnlock.ItemId),
                            ActivityLogEF.Legacy.CraftingCompletedEntry craftingComplete => new ActivityLogEF.CraftingCompletedEntry(craftingComplete.Timestamp, craftingComplete.Rewards),
                            ActivityLogEF.Legacy.SmeltingCompletedEntry smeltingComplete => new ActivityLogEF.SmeltingCompletedEntry(smeltingComplete.Timestamp, smeltingComplete.Rewards),
                            ActivityLogEF.Legacy.BoostActivatedEntry boostActivated => new ActivityLogEF.BoostActivatedEntry(boostActivated.Timestamp, boostActivated.ItemId),
                            _ => throw new UnreachableException(),
                        });
                    }
                }

                break;
            case "buildplates":
                {
                    var value = ReadLegacyDbJson<LegacyBuildplates>(valueString);

                    var id = GetId(idString);

                    var buildplates = (await _earthDb.GetOrCreateAccount(id, query => query.Include(account => account.Buildplates)))
                        .Buildplates!;

                    foreach (var (buildplateId, buildplate) in value.Buildplates)
                    {
                        buildplates.Add(new BuildplateEF()
                        {
                            Id = Guid.Parse(buildplateId),
                            AccountId = id,
                            TemplateId = buildplate.TemplateId is null ? null : Guid.Parse(buildplate.TemplateId),
                            Name = buildplate.Name ?? "buildplate",
                            Size = buildplate.Size,
                            Offset = buildplate.Offset,
                            Scale = buildplate.Scale,
                            Night = buildplate.Night,
                            LastModified = buildplate.LastModified,
                            ServerDataObjectId = buildplate.ServerDataObjectId,
                            PreviewObjectId = buildplate.PreviewObjectId,
                        });
                    }
                }

                break;
            case "sharedBuildplates":
                {
                    Debug.Assert(string.IsNullOrWhiteSpace(idString));

                    var value = ReadLegacyDbJson<LegacySharedBuildplates>(valueString);

                    foreach (var (buildplateId, buildplate) in value.SharedBuildplates)
                    {
                        var accountId = GetId(buildplate.PlayerId);

                        var sharedBuildplates = (await _earthDb.GetOrCreateAccount(accountId, query => query.Include(account => account.SharedBuildplates)))
                            .SharedBuildplates!;

                        sharedBuildplates.Add(new SharedBuildplateEF()
                        {
                            Id = Guid.Parse(buildplateId),
                            AccountId = accountId,
                            Size = buildplate.Size,
                            Offset = buildplate.Offset,
                            Scale = buildplate.Scale,
                            Night = buildplate.Night,
                            Created = buildplate.Created,
                            BuildplateLastModifed = buildplate.BuildplateLastModifed,
                            LastViewed = buildplate.LastViewed,
                            NumberOfTimesViewed = buildplate.NumberOfTimesViewed,
                            Hotbar = [.. buildplate.Hotbar.Select(item => item is null ? null : new SharedBuildplateEF.HotbarItem(item.Uuid, item.Count, item.InstanceId, item.Wear))],
                            ServerDataObjectId = buildplate.ServerDataObjectId,
                        });

                        await _earthDb.SaveChangesAsync();
                    }
                }

                break;
            case "encounterBuildplates":
                {
                    Debug.Assert(string.IsNullOrWhiteSpace(idString));

                    var value = ReadLegacyDbJson<LegacyEncounterBuildplates>(valueString);

                    foreach (var (encounterId, encounter) in value.EncounterBuildplates)
                    {
                        _earthDb.EncounterBuildplates.Add(new EncounterBuildplateEF()
                        {
                            Id = Guid.Parse(encounterId),
                            Size = encounter.Size,
                            Offset = encounter.Offset,
                            Scale = encounter.Scale,
                            ServerDataObjectId = encounter.ServerDataObjectId,
                        });
                    }

                    await _earthDb.SaveChangesAsync();
                }

                break;
            default:
                {
                    Log.Warning($"Unknown object type '{type}', will not be migrated.");
                }

                break;
        }
    }

    private Guid GetId(string idString)
    {
        if (Guid.TryParse(idString, out var id) || _oldToNewId.TryGetValue(idString, out id))
        {
            return id;
        }

        id = Guid.CreateVersion7();

        _oldToNewId.Add(idString, id);

        return id;
    }
}