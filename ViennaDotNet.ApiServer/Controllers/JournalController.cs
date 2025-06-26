using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
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
        journalModel.Items.ForEach((uuid, itemJournalEntry) => inventoryJournal[uuid] = new Types.Journal.JournalRecord.InventoryJournalEntry(
            TimeFormatter.FormatTime(itemJournalEntry.FirstSeen),
            TimeFormatter.FormatTime(itemJournalEntry.LastSeen),
            itemJournalEntry.AmountCollected
        ));

        LinkedList<Types.Journal.JournalRecord.ActivityLogEntry> _activityLogList = activityLogModel.Entries
            .Select(ActivityLogEntryToApiResponse)
            .Collect(() => new LinkedList<Types.Journal.JournalRecord.ActivityLogEntry>(), (list, val) => list.AddLast(val), (list1, list2) => list1.AddRange(list1));

        var activityLogList = _activityLogList.Reverse().ToArray();
        Types.Journal.JournalRecord.ActivityLogEntry[] activityLog = activityLogList;

        string resp = Json.Serialize(new EarthApiResponse(new Types.Journal.JournalRecord(inventoryJournal, activityLog)));
        return Content(resp, "application/json");
    }

    private static Types.Journal.JournalRecord.ActivityLogEntry ActivityLogEntryToApiResponse(ActivityLog.Entry entry)
    {
        Rewards rewards = entry.Type switch
        {
            ActivityLog.Entry.TypeE.LEVEL_UP => new Rewards().setLevel(((ActivityLog.LevelUpEntry)entry).Level),
            ActivityLog.Entry.TypeE.TAPPABLE => Rewards.FromDBRewardsModel(((ActivityLog.TappableEntry)entry).Rewards),
            ActivityLog.Entry.TypeE.JOURNAL_ITEM_UNLOCKED => new Rewards().addItem(((ActivityLog.JournalItemUnlockedEntry)entry).ItemId, 0),
            ActivityLog.Entry.TypeE.CRAFTING_COMPLETED => Rewards.FromDBRewardsModel(((ActivityLog.CraftingCompletedEntry)entry).Rewards),
            ActivityLog.Entry.TypeE.SMELTING_COMPLETED => Rewards.FromDBRewardsModel(((ActivityLog.SmeltingCompletedEntry)entry).Rewards),
            ActivityLog.Entry.TypeE.BOOST_ACTIVATED => new Rewards(),
            _ => throw new InvalidDataException($"Unknown ActivityLog.Entry.Type '{entry.Type}'"),
        };

        Dictionary<string, string> properties = [];
        switch (entry.Type)
        {
            case ActivityLog.Entry.TypeE.BOOST_ACTIVATED:
                {
                    properties["boostId"] = ((ActivityLog.BoostActivatedEntry)entry).ItemId;
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
