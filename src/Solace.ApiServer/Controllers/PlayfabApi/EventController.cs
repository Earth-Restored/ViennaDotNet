using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ViennaDotNet.ApiServer.Models.Playfab;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.ApiServer.Controllers.PlayfabApi;

[Route("Event")]
[Route("20CA2.playfabapi.com/Event")]
public class EventController : ViennaControllerBase
{

    private sealed record WriteTelemetryEventsRequest(
        object[] Events
    );

    [HttpPost("WriteTelemetryEvents")]
    public async Task<Results<ContentHttpResult, BadRequest>> WriteTelemetryEvents()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<WriteTelemetryEventsRequest>(cancellationToken);

        if (request is null)
        {
            return TypedResults.BadRequest();
        }

        return JsonPascalCase(new PlayfabOkResponse(
            200,
            "OK",
            new Dictionary<string, object>()
            {
                ["AssignedEventIds"] = request.Events.Select(_ => Guid.NewGuid().ToString("N")),
            }
        ));
    }
}
