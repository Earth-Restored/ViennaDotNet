using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.DB;

namespace ViennaDotNet.ApiServer.Utils
{
    public static class ActivityLogUtils
    {
        public static EarthDB.Query addEntry(string playerId, ActivityLog.Entry entry)
        {
            EarthDB.Query getQuery = new EarthDB.Query(true);
            getQuery.Get("activityLog", playerId, typeof(ActivityLog));
            getQuery.Then(results =>
            {
                ActivityLog activityLog = (ActivityLog)results.Get("activityLog").Value;
                activityLog.addEntry(entry);
                EarthDB.Query updateQuery = new EarthDB.Query(true);
                updateQuery.Update("activityLog", playerId, activityLog);
                return updateQuery;
            });
            return getQuery;
        }
    }
}
