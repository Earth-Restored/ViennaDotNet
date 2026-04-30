using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;

namespace ViennaDotNet.ApiServer.Utils;

public static class ActivityLogUtils
{
    public static EarthDB.Query AddEntry(string playerId, ActivityLog.Entry entry)
    {
        EarthDB.Query getQuery = new EarthDB.Query(true);
        getQuery.Get("activityLog", playerId, typeof(ActivityLog));
        getQuery.Then(results =>
        {
            ActivityLog activityLog = results.Get<ActivityLog>("activityLog");
            activityLog.AddEntry(entry);
            activityLog.Prune();
            EarthDB.Query updateQuery = new EarthDB.Query(true);
            updateQuery.Update("activityLog", playerId, activityLog);
            return updateQuery;
        });
        return getQuery;
    }
}
