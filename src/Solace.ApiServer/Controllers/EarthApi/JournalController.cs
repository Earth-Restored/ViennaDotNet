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
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Solace.DB.Utils;

namespace Solace.ApiServer.Controllers.EarthApi;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/player/journal")]
internal sealed class JournalController : SolaceControllerBase
{
    private readonly EarthDbContext _earthDB;

    public JournalController(EarthDbContext earthDB)
    {
        _earthDB = earthDB;
    }

    [HttpGet]
    public async Task<Results<ContentHttpResult, BadRequest>> Get(CancellationToken cancellationToken)
    {
        if (!TryGetAccountId(out var accountId))
        {
            return TypedResults.BadRequest();
        }

        var journal = await _earthDB.Journals
            .AsNoTracking()
            .FirstOrNewAsync(journal => journal.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        var activityLogs = await _earthDB.ActivityLogs
            .AsNoTracking()
            .FirstOrNewAsync(activityLogs => activityLogs.Id == accountId, trackNew: false, cancellationToken: cancellationToken);

        Dictionary<string, Types.Journal.JournalRecord.InventoryJournalEntry> inventoryJournal = [];
        foreach (var (uuid, itemJournalEntry) in journal.Items)
        {
            inventoryJournal[uuid] = new Types.Journal.JournalRecord.InventoryJournalEntry(
                TimeFormatter.FormatTime(itemJournalEntry.FirstSeen),
                TimeFormatter.FormatTime(itemJournalEntry.LastSeen),
                itemJournalEntry.AmountCollected
            );
        }

        Types.Journal.JournalRecord.ActivityLogEntry[] activityLog = [.. activityLogs.Entries.Select(ActivityLogEntryToApiResponse)];
        Array.Reverse(activityLog);

        string resp = Json.Serialize(new EarthApiResponse(new Types.Journal.JournalRecord(inventoryJournal, activityLog)));
        return TypedResults.Content(resp, "application/json");
    }

    private static Types.Journal.JournalRecord.ActivityLogEntry ActivityLogEntryToApiResponse(ActivityLogEF.Entry entry)
    {
        Rewards rewards = entry switch
        {
            ActivityLogEF.LevelUpEntry levelUp => new Rewards().SetLevel(levelUp.Level),
            ActivityLogEF.TappableEntry tappable => Rewards.FromDBRewardsModel(tappable.Rewards),
            ActivityLogEF.JournalItemUnlockedEntry journalItemUnlocked => new Rewards().AddItem(journalItemUnlocked.ItemId, 0),
            ActivityLogEF.CraftingCompletedEntry craftingCompleted => Rewards.FromDBRewardsModel(craftingCompleted.Rewards),
            ActivityLogEF.SmeltingCompletedEntry smeltingCompleted => Rewards.FromDBRewardsModel(smeltingCompleted.Rewards),
            ActivityLogEF.BoostActivatedEntry => new Rewards(),
            _ => throw new InvalidDataException($"Unknown ActivityLog.Entry '{entry?.GetType()?.ToString() ?? "null"}'"),
        };

        Dictionary<string, string> properties = [];
        switch (entry)
        {
            case ActivityLogEF.BoostActivatedEntry boostActivated:
                {
                    properties["boostId"] = boostActivated.ItemId;
                }

                break;
        }

        return new Types.Journal.JournalRecord.ActivityLogEntry(
            entry.Type switch
            {
                ActivityLogEF.Entry.TypeE.LEVEL_UP => Types.Journal.JournalRecord.ActivityLogEntry.Type.LEVEL_UP,
                ActivityLogEF.Entry.TypeE.TAPPABLE => Types.Journal.JournalRecord.ActivityLogEntry.Type.TAPPABLE,
                ActivityLogEF.Entry.TypeE.JOURNAL_ITEM_UNLOCKED => Types.Journal.JournalRecord.ActivityLogEntry.Type.JOURNAL_ITEM_UNLOCKED,
                ActivityLogEF.Entry.TypeE.CRAFTING_COMPLETED => Types.Journal.JournalRecord.ActivityLogEntry.Type.CRAFTING_COMPLETED,
                ActivityLogEF.Entry.TypeE.SMELTING_COMPLETED => Types.Journal.JournalRecord.ActivityLogEntry.Type.SMELTING_COMPLETED,
                ActivityLogEF.Entry.TypeE.BOOST_ACTIVATED => Types.Journal.JournalRecord.ActivityLogEntry.Type.BOOST_ACTIVATED,
                _ => throw new UnreachableException(),
            },
            TimeFormatter.FormatTime(entry.Timestamp),
            rewards.ToApiResponse(),
            properties
        );
    }
}
