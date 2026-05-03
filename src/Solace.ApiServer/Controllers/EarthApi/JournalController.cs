using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Solace.ApiServer.Exceptions;
using Solace.ApiServer.Utils;
using Solace.Common;
using Solace.Common.Utils;
using Solace.DB;
using Solace.DB.Models.Player;

namespace Solace.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/player/journal")]
internal sealed class JournalController : ControllerBase
{
    private static EarthDB earthDB => Program.DB;

    [HttpGet]
    public async Task<Results<ContentHttpResult, BadRequest>> Get(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return TypedResults.BadRequest();
        }

        Journal journalModel;
        ActivityLog activityLogModel;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("journal", playerId, typeof(Journal))
                .Get("activityLog", playerId, typeof(ActivityLog))
                .ExecuteAsync(earthDB, cancellationToken);

            journalModel = results.Get<Journal>("journal");
            activityLogModel = results.Get<ActivityLog>("activityLog");
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        Dictionary<string, Types.Journal.JournalRecord.InventoryJournalEntry> inventoryJournal = [];
        journalModel.Items.ForEach((uuid, itemJournalEntry) => inventoryJournal[uuid] = new Types.Journal.JournalRecord.InventoryJournalEntry(
            TimeFormatter.FormatTime(itemJournalEntry.FirstSeen),
            TimeFormatter.FormatTime(itemJournalEntry.LastSeen),
            itemJournalEntry.AmountCollected
        ));

        LinkedList<Types.Journal.JournalRecord.ActivityLogEntry> _activityLogList = new(activityLogModel.Entries
            .Select(ActivityLogEntryToApiResponse));

        var activityLogList = _activityLogList.Reverse().ToArray();
        Types.Journal.JournalRecord.ActivityLogEntry[] activityLog = activityLogList;

        string resp = Json.Serialize(new EarthApiResponse(new Types.Journal.JournalRecord(inventoryJournal, activityLog)));
        return TypedResults.Content(resp, "application/json");
    }

    private static Types.Journal.JournalRecord.ActivityLogEntry ActivityLogEntryToApiResponse(ActivityLog.Entry entry)
    {
        Rewards rewards = entry switch
        {
            ActivityLog.LevelUpEntry levelUp => new Rewards().SetLevel(levelUp.Level),
            ActivityLog.TappableEntry tappable => Rewards.FromDBRewardsModel(tappable.Rewards),
            ActivityLog.JournalItemUnlockedEntry journalItemUnlocked => new Rewards().AddItem(journalItemUnlocked.ItemId, 0),
            ActivityLog.CraftingCompletedEntry craftingCompleted => Rewards.FromDBRewardsModel(craftingCompleted.Rewards),
            ActivityLog.SmeltingCompletedEntry smeltingCompleted => Rewards.FromDBRewardsModel(smeltingCompleted.Rewards),
            ActivityLog.BoostActivatedEntry => new Rewards(),
            _ => throw new InvalidDataException($"Unknown ActivityLog.Entry '{entry?.GetType()?.ToString() ?? "null"}'"),
        };

        Dictionary<string, string> properties = [];
        switch (entry)
        {
            case ActivityLog.BoostActivatedEntry boostActivated:
                {
                    properties["boostId"] = boostActivated.ItemId;
                }

                break;
        }

        return new Types.Journal.JournalRecord.ActivityLogEntry(
            Enum.Parse<Types.Journal.JournalRecord.ActivityLogEntry.Type>(entry.Type.ToString()),
            TimeFormatter.FormatTime(entry.Timestamp),
            rewards.ToApiResponse(),
            properties
        );
    }
}
