using Solace.DB;
using Solace.DB.Models.Player;

namespace Solace.ApiServer.Utils;

public static class ActivityLogUtils
{
    public static EarthDB.Query AddEntry(string playerId, ActivityLog.Entry entry)
    {
        var getQuery = new EarthDB.Query(true);
        getQuery.Get("activityLog", playerId, typeof(ActivityLog));
        getQuery.Then(results =>
        {
            ActivityLog activityLog = results.Get<ActivityLog>("activityLog");
            activityLog.AddEntry(entry);
            activityLog.Prune();
            var updateQuery = new EarthDB.Query(true);
            updateQuery.Update("activityLog", playerId, activityLog);
            return updateQuery;
        });
        return getQuery;
    }
}
