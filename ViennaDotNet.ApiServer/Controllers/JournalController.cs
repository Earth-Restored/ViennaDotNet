using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/player/journal")]
public class JournalController : ControllerBase
{
    private static EarthDB earthDB => Program.DB;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        Journal journalModel;
        ActivityLog activityLogModel;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("journal", playerId, typeof(Journal))
                .Get("activityLog", playerId, typeof(ActivityLog))
                .ExecuteAsync(earthDB, cancellationToken);

            journalModel = (Journal)results.Get("journal").Value;
            activityLogModel = (ActivityLog)results.Get("activityLog").Value;
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        Dictionary<string, Types.Journal.JournalRecord.InventoryJournalEntry> inventoryJournal = [];
        journalModel.getItems().ForEach((uuid, itemJournalEntry) => inventoryJournal[uuid] = new Types.Journal.JournalRecord.InventoryJournalEntry(
            TimeFormatter.FormatTime(itemJournalEntry.firstSeen),
            TimeFormatter.FormatTime(itemJournalEntry.lastSeen),
            itemJournalEntry.amountCollected
        ));

        LinkedList<Types.Journal.JournalRecord.ActivityLogEntry> _activityLogList = activityLogModel.getEntries()
            .Select(activityLogEntryToApiResponse)
            .Collect(() => new LinkedList<Types.Journal.JournalRecord.ActivityLogEntry>(), (list, val) => list.AddLast(val), (list1, list2) => list1.AddRange(list1));
        var activityLogList = _activityLogList.Reverse().ToArray();
        Types.Journal.JournalRecord.ActivityLogEntry[] activityLog = activityLogList;

        string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Types.Journal.JournalRecord(inventoryJournal, activityLog)));
        return Content(resp, "application/json");
    }

    private static Types.Journal.JournalRecord.ActivityLogEntry activityLogEntryToApiResponse(ActivityLog.Entry entry)
    {
        Rewards rewards = entry.type switch
        {
            ActivityLog.Entry.Type.LEVEL_UP => new Rewards().setLevel(((ActivityLog.LevelUpEntry)entry).level),
            ActivityLog.Entry.Type.TAPPABLE => Rewards.fromDBRewardsModel(((ActivityLog.TappableEntry)entry).rewards),
            ActivityLog.Entry.Type.JOURNAL_ITEM_UNLOCKED => new Rewards().addItem(((ActivityLog.JournalItemUnlockedEntry)entry).itemId, 0),
            ActivityLog.Entry.Type.CRAFTING_COMPLETED => Rewards.fromDBRewardsModel(((ActivityLog.CraftingCompletedEntry)entry).rewards),
            ActivityLog.Entry.Type.SMELTING_COMPLETED => Rewards.fromDBRewardsModel(((ActivityLog.SmeltingCompletedEntry)entry).rewards),
            ActivityLog.Entry.Type.BOOST_ACTIVATED => new Rewards(),
            _ => throw new InvalidDataException($"Unknown ActivityLog.Entry.Type '{entry.type}'"),
        };

        Dictionary<string, string> properties = [];
        switch (entry.type)
        {
            case ActivityLog.Entry.Type.BOOST_ACTIVATED:
                {
                    properties["boostId"] = ((ActivityLog.BoostActivatedEntry)entry).itemId;
                }

                break;
        }

        return new Types.Journal.JournalRecord.ActivityLogEntry(
            Enum.Parse<Types.Journal.JournalRecord.ActivityLogEntry.Type>(entry.type.ToString()),
            TimeFormatter.FormatTime(entry.timestamp),
            rewards.toApiResponse(),
            properties
        );
    }
}
