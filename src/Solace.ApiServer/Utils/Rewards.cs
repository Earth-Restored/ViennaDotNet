using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Common;
using Solace.DB.Models.Player;
using Solace.DB.Utils;
using Solace.StaticData;

namespace Solace.ApiServer.Utils;

public sealed class Rewards
{
    private int _rubies;
    private int _experiencePoints;

    private int? _level;
    private readonly Dictionary<string, int?> _items = [];
    private readonly HashSet<string> _buildplates = [];
    private readonly HashSet<string> _challenges = [];

    public Rewards()
    {
        // empty
    }

    public Rewards SetLevel(int level)
    {
        _level = level;
        return this;
    }

    public Rewards AddItem(string id, int count)
    {
        _items[id] = _items.GetOrDefault(id, 0) + count;
        return this;
    }

    public Rewards AddBuildplate(string id)
    {
        _buildplates.Add(id);
        return this;
    }

    public Rewards AddChallenge(string id)
    {
        _challenges.Add(id);
        return this;
    }

    public Rewards AddRubies(int rubies)
    {
        _rubies += rubies;
        return this;
    }

    public Rewards AddExperiencePoints(int experiencePoints)
    {
        _experiencePoints += experiencePoints;
        return this;
    }

    public async Task ToRedeemQueryAsync(EarthDbContext.Results results, Guid accountId, long currentTime, StaticData.StaticData staticData)
    {
        ProfileEF? profile = null;
        if (_rubies > 0 || _experiencePoints > 0)
        {
            profile = await results.EarthDb.Profiles
                .AsTracking()
                .FirstOrNewAsync(profile => profile.Id == accountId);
        }

        InventoryEF? inventory = null;
        JournalEF? journal = null;
        if (!_items.IsEmpty())
        {
            inventory = await results.EarthDb.Inventories
                .AsTracking()
                .FirstOrNewAsync(inventory => inventory.Id == accountId);

            journal = await results.EarthDb.Journals
                .AsTracking()
                .FirstOrNewAsync(journal => journal.Id == accountId);
        }

        if (!_buildplates.IsEmpty())
        {
            // TODO
        }

        if (!_challenges.IsEmpty())
        {
            // TODO
        }

        bool checkLevelUp = false;
        if (_rubies > 0 || _experiencePoints > 0)
        {
            Debug.Assert(profile is not null);

            if (_rubies > 0)
            {
                profile.Rubies.Earned += _rubies;
            }

            if (_experiencePoints > 0)
            {
                profile.Experience += _experiencePoints;
            }

            if (_experiencePoints > 0)
            {
                checkLevelUp = true;
            }

            await results.EarthDb.SaveChangesAsync();

            results.Profile = profile.Version;
        }

        if (!_items.IsEmpty())
        {
            Debug.Assert(inventory is not null);
            Debug.Assert(journal is not null);

            foreach (var entry in _items)
            {
                string id = entry.Key;
                int quantity = entry.Value ?? 0; // idk, no null checks here, so I added ?? 0
                if (quantity > 0)
                {
                    Catalog.ItemsCatalogR.Item? item = staticData.Catalog.ItemsCatalog.GetItem(id);
                    Debug.Assert(item is not null);

                    if (item.Stackable)
                    {
                        inventory.AddItems(id, quantity);
                    }
                    else
                    {
                        inventory.AddItems(id, [.. Enumerable.Range(0, quantity).Select(index => new NonStackableItemInstance(Guid.NewGuid().ToString(), 0))]);
                    }

                    if (journal.AddCollectedItem(id, currentTime, quantity) == 0)
                    {
                        if (item.JournalEntry is not null)
                        {
                            await TokenUtils.AddTokenAsync(results, accountId, new TokensEF.JournalItemUnlockedToken(id));
                        }
                    }
                }
            }

            await results.EarthDb.SaveChangesAsync();

            results.Inventory = inventory.Version;
            results.Journal = journal.Version;
        }

        if (!_buildplates.IsEmpty())
        {
            // TODO
        }

        if (!_challenges.IsEmpty())
        {
            // TODO
        }

        if (checkLevelUp)
        {
            await LevelUtils.CheckAndHandlePlayerLevelUpAsync(results, accountId, currentTime, staticData);
        }
    }

    public Types.Common.Rewards ToApiResponse()
        => new Types.Common.Rewards(
            _rubies,
            _experiencePoints,
            _level,
            [.. _items.Select(item => new Types.Common.Rewards.Item(item.Key, item.Value ?? 0))],
            [.. _buildplates],
            [.. _challenges.Select(challenge => new Types.Common.Rewards.Challenge(challenge))],
            [],
            []
        );

    public static Rewards FromDBRewardsModel(DB.Models.Common.Rewards rewardsModel)
    {
        var rewards = new Rewards();
        rewards.AddRubies(rewardsModel.Rubies);
        rewards.AddExperiencePoints(rewardsModel.ExperiencePoints);
        if (rewardsModel.Level is not null)
        {
            rewards.SetLevel(rewardsModel.Level.Value);
        }

        rewardsModel.Items.ForEach((id, count) => rewards.AddItem(id, count ?? 0));
        Array.ForEach(rewardsModel.Buildplates, id => rewards.AddBuildplate(id));
        Array.ForEach(rewardsModel.Challenges, id => rewards.AddChallenge(id));
        return rewards;
    }

    public DB.Models.Common.Rewards ToDBRewardsModel()
        => new DB.Models.Common.Rewards(
            _rubies,
            _experiencePoints,
            _level,
            new(_items),
            [.. _buildplates],
            [.. _challenges]
        );
}
