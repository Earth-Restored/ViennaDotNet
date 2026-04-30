using System.Diagnostics;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Utils;

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

    public EarthDB.Query ToRedeemQuery(string playerId, long currentTime, StaticData.StaticData staticData)
    {
        EarthDB.Query getQuery = new EarthDB.Query(true);
        if (_rubies > 0 || _experiencePoints > 0)
        {
            getQuery.Get("profile", playerId, typeof(Profile));
        }

        if (!_items.IsEmpty())
        {
            getQuery.Get("inventory", playerId, typeof(Inventory));
            getQuery.Get("journal", playerId, typeof(Journal));
        }

        if (!_buildplates.IsEmpty())
        {
            // TODO
        }

        if (!_challenges.IsEmpty())
        {
            // TODO
        }

        EarthDB.Query updateQuery = new EarthDB.Query(true);
        getQuery.Then(results =>
        {
            bool checkLevelUp = false;
            if (_rubies > 0 || _experiencePoints > 0)
            {
                Profile profile = results.Get<Profile>("profile");
                if (_rubies > 0)
                {
                    profile.Rubies.Earned += _rubies;
                }

                if (_experiencePoints > 0)
                {
                    profile.Experience += _experiencePoints;
                }

                updateQuery.Update("profile", playerId, profile);

                if (_experiencePoints > 0)
                {
                    checkLevelUp = true;
                }
            }

            if (!_items.IsEmpty())
            {
                Inventory inventory = results.Get<Inventory>("inventory");
                Journal journal = results.Get<Journal>("journal");
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
                            inventory.AddItems(id, [.. Java.IntStream.Range(0, quantity).Select(index => new NonStackableItemInstance(U.RandomUuid().ToString(), 0))]);
                        }

                        if (journal.AddCollectedItem(id, currentTime, quantity) == 0)
                        {
                            if (item.JournalEntry is not null)
                            {
                                updateQuery.Then(TokenUtils.AddToken(playerId, new Tokens.JournalItemUnlockedToken(id)));
                            }
                        }
                    }
                }

                updateQuery.Update("inventory", playerId, inventory);
                updateQuery.Update("journal", playerId, journal);
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
                updateQuery.Then(LevelUtils.CheckAndHandlePlayerLevelUp(playerId, currentTime, staticData));
            }

            return updateQuery;
        }, false);
        getQuery.Extra("rewards", this);

        return getQuery;
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
        Rewards rewards = new Rewards();
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
