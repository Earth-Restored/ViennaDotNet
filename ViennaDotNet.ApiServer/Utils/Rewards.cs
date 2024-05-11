using ViennaDotNet.ApiServer.Types.Catalog;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Player;

namespace ViennaDotNet.ApiServer.Utils
{
    public sealed class Rewards
    {
        private int rubies;
        private int experiencePoints;

        private int? level;
        private Dictionary<string, int?> items = new();
        private HashSet<string> buildplates = new();
        private HashSet<string> challenges = new();

        public Rewards()
        {
            // empty
        }

        public Rewards setLevel(int level)
        {
            this.level = level;
            return this;
        }

        public Rewards addItem(string id, int count)
        {
            items[id] = items.GetOrDefault(id, 0) + count;
            return this;
        }

        public Rewards addBuildplate(string id)
        {
            buildplates.Add(id);
            return this;
        }

        public Rewards addChallenge(string id)
        {
            challenges.Add(id);
            return this;
        }

        public Rewards addRubies(int rubies)
        {
            this.rubies += rubies;
            return this;
        }

        public Rewards addExperiencePoints(int experiencePoints)
        {
            this.experiencePoints += experiencePoints;
            return this;
        }

        public EarthDB.Query toRedeemQuery(string playerId, long currentTime, Catalog catalog)
        {
            EarthDB.Query getQuery = new EarthDB.Query(true);
            if (rubies > 0 || experiencePoints > 0)
                getQuery.Get("profile", playerId, typeof(Profile));

            if (!items.IsEmpty())
            {
                getQuery.Get("inventory", playerId, typeof(Inventory));
                getQuery.Get("journal", playerId, typeof(Journal));
            }

            if (!buildplates.IsEmpty())
            {
                // TODO
            }
            if (!challenges.IsEmpty())
            {
                // TODO
            }

            EarthDB.Query updateQuery = new EarthDB.Query(true);
            getQuery.Then(results =>
            {
                if (rubies > 0 || experiencePoints > 0)
                {
                    Profile profile = (Profile)results.Get("profile").Value;
                    if (rubies > 0)
                        profile.rubies.earned += rubies;

                    if (experiencePoints > 0)
                        profile.experience += experiencePoints;

                    updateQuery.Update("profile", playerId, profile);

                    if (experiencePoints > 0)
                        updateQuery.Then(LevelUtils.checkAndHandlePlayerLevelUp(playerId, currentTime, catalog));
                }

                if (!items.IsEmpty())
                {
                    Inventory inventory = (Inventory)results.Get("inventory").Value;
                    Journal journal = (Journal)results.Get("journal").Value;
                    foreach (var entry in items)
                    {
                        string id = entry.Key;
                        int quantity = entry.Value ?? 0; // idk, no null checks here, so I added ?? 0
                        if (quantity > 0)
                        {
                            ItemsCatalog.Item item = catalog.itemsCatalog.items.Where(item1 => item1.id == id).First();
                            if (item.stacks)
                                inventory.addItems(id, quantity);
                            else
                                inventory.addItems(id, Java.IntStream.Range(0, quantity).Select(index => new NonStackableItemInstance(U.RandomUuid().ToString(), 0)).ToArray());

                            journal.touchItem(id, currentTime);
                            // TODO: should probably wrap this in a "JournalUtils" class so that incrementing the counter and adding the item unlocked token for new items are always handled together, in case this is ever required from somewhere other than Rewards
                            if (journal.getItem(id)!.amountCollected == 0)
                            {
                                updateQuery.Then(TokenUtils.addToken(playerId, new Tokens.Token(Tokens.Token.Type.JOURNAL_ITEM_UNLOCKED, new Rewards().toDBRewardsModel(), Tokens.Token.Lifetime.PERSISTENT, new Dictionary<string, string>() { 
                                    { "itemid", id } 
                                })));
                            }
                            journal.addCollectedItem(id, quantity);
                        }
                    }
                    updateQuery.Update("inventory", playerId, inventory);
                    updateQuery.Update("journal", playerId, journal);
                }

                if (!buildplates.IsEmpty())
                {
                    // TODO
                }

                if (!challenges.IsEmpty())
                {
                    // TODO
                }

                return updateQuery;
            });
            getQuery.Then(new EarthDB.Query(false).Extra("rewards", this));

            return getQuery;
        }

        public Types.Common.Rewards toApiResponse()
        {
            return new Types.Common.Rewards(
                rubies,
                experiencePoints,
                level,
                items.Select(item => new Types.Common.Rewards.Item(item.Key, item.Value ?? 0)).ToArray(),
                buildplates.Select(buildplate => new Types.Common.Rewards.Buildplate(buildplate)).ToArray(),
                challenges.Select(challenge => new Types.Common.Rewards.Challenge(challenge)).ToArray(),
                Array.Empty<string>(),
                Array.Empty<Types.Common.Rewards.UtilityBlock>()
            );
        }

        public static Rewards fromDBRewardsModel(DB.Models.Common.Rewards rewardsModel)
        {
            Rewards rewards = new Rewards();
            rewards.addRubies(rewardsModel.rubies);
            rewards.addExperiencePoints(rewardsModel.experiencePoints);
            if (rewardsModel.level is not null)
                rewards.setLevel(rewardsModel.level.Value);

            rewardsModel.items.ForEach((id, count) => rewards.addItem(id, count ?? 0));
            Array.ForEach(rewardsModel.buildplates, id => rewards.addBuildplate(id));
            Array.ForEach(rewardsModel.challenges, id => rewards.addChallenge(id));
            return rewards;
        }

        public DB.Models.Common.Rewards toDBRewardsModel()
        {
            return new DB.Models.Common.Rewards(
                rubies,
                experiencePoints,
                level,
                new(items),
                buildplates.ToArray(),
                challenges.ToArray()
            );
        }
    }
}
