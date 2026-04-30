using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ViennaDotNet.LauncherUI.Controllers;

[ApiController]
[Route("api/logs")]
public class LogController : ControllerBase
{
    private readonly LogsLogService _logService;

    public LogController(LogsLogService logService)
    {
        _logService = logService;
    }

    [HttpPost("create")]
    public Results<Ok, BadRequest<string>> CreateLogs([FromBody] LogEvent[] body)
    {
        if (body == null || body.Length == 0)
        {
            return TypedResults.BadRequest("Log payload cannot be empty.");
        }

        _logService.AddExternalLogs(body);

        return TypedResults.Ok();
    }
}