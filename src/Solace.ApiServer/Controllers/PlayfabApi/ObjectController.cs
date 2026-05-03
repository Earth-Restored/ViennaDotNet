using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Solace.ApiServer.Models.Playfab;
using Solace.Common.Utils;

namespace Solace.ApiServer.Controllers.PlayfabApi;

[Route("Object")]
[Route("20CA2.playfabapi.com/Object")]
internal sealed class ObjectController : SolaceControllerBase
{
    private sealed record GetObjectsRequest(
        GetObjectsRequest.EntityR Entity,
        object? EscapeObject
    )
    {
        public sealed record EntityR(
            string Id,
            string Type
        );
    }

    private static readonly string[] DataArray0 = ["#ff523d89", "#0", "#0", "#0"];
    private static readonly string[] DataArray1 = ["#ff2f1f0f", "#0", "#0", "#0"];
    private static readonly string[] DataArray2 = ["#0", "#0", "#ff774235", "#0"];

    [HttpPost("GetObjects")]
    public async Task<Results<ContentHttpResult, ForbidHttpResult, BadRequest>> GetObjects()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<GetObjectsRequest>(cancellationToken);

        if (request is null)
        {
            return TypedResults.BadRequest();
        }

        var tokenUnion = PlayfabAuth();

        if (tokenUnion.IsB)
        {
            return tokenUnion.B.Result is ForbidHttpResult forbid ? forbid : (BadRequest)tokenUnion.B.Result;
        }

        var token = tokenUnion.A;

        if (token.Id != request.Entity.Id || token.Type != request.Entity.Type)
        {
            return TypedResults.Forbid();
        }

        return request.Entity.Type switch
        {
            "master_player_account" => JsonPascalCase(new PlayfabOkResponse(
                    200,
                    "OK",
                    new Dictionary<string, object>()
                    {
                        ["ProfileVersion"] = 172,
                        ["Objects"] = new Dictionary<string, object>
                        {
                            ["personaProfile"] = new Dictionary<string, object>
                            {
                                ["ObjectName"] = "personaProfile",
                                ["DataObject"] = new Dictionary<string, object>
                                {
                                    ["personaCollection"] = new Dictionary<string, object>
                                    {
                                        ["universalApp"] = new object[]
                                        {
                                new Dictionary<string, object>
                                {
                                    ["id"] = "00000000-0000-0000-0000-000000000000_",
                                    ["isPlatformLocked"] = false,
                                    ["isTitleLocked"] = false,
                                    ["lastUsedPersonaSlot"] = "persona_profile_persona2",
                                    ["packId"] = "00000000-0000-0000-0000-000000000000",
                                    ["typeId"] = "skin"
                                },
                                        },
                                    },
                                    ["version"] = "0.0.1",
                                }
                            },
                            ["personaProfile2"] = new Dictionary<string, object>
                            {
                                ["ObjectName"] = "personaProfile2",
                                ["DataObject"] = new Dictionary<string, object>
                                {
                                    ["slotname"] = "",
                                    ["personaCollection"] = new Dictionary<string, object?>
                                    {
                                        ["universalApp"] = new object[]
                                        {
                                new Dictionary<string, object>
                                {
                                    ["arm"] = "wide",
                                    ["skcol"] = "#ffb37b62",
                                    ["skin"] = false,
                                },
                                new Dictionary<string, object> { ["id"] = "c18e65aa-7b21-4637-9b63-8ad63622ef01.Custome4e3c9cbcb5b4b179a1034a6d5ef3d29" ,},
                                new Dictionary<string, object> { ["id"] = "8f96d1f8-e9bb-40d2-acc8-eb79746c5d7c/d" ,},
                                new Dictionary<string, object> { ["id"] = "1042557f-d1f9-44e3-ba78-f404e8fb7363/d" ,},
                                new Dictionary<string, object> { ["id"] = "f1e4c577-19ba-4d77-9222-47f145857f78/d" ,},
                                new Dictionary<string, object> { ["id"] = "49f93789-a512-4c47-95cb-0606cdc1c2be/d", },
                                new Dictionary<string, object> { ["id"] = "68bfe60d-f30a-422f-b32c-72374ebdd057/d", },
                                new Dictionary<string, object> { ["id"] = "b6702f0e-a4b5-497a-8820-6c8e3946bb55/d", },
                                new Dictionary<string, object>
                                {
                                    ["col"] = DataArray0,
                                    ["id"] = "a0f263b3-e093-4c85-aadb-3759417898ff/d",
                                },
                                new Dictionary<string, object>
                                {
                                    ["col"] = DataArray1,
                                    ["id"] = "2bb1473b-9a5c-4eae-9fd5-82302a6aa3da/d",
                                },
                                new Dictionary<string, object>
                                {
                                    ["col"] = DataArray2,
                                    ["id"] = "2e4341c3-c014-4114-94b5-73968230aaaf/d",
                                },
                                        },
                                        ["tplApps"] = null,
                                        ["tlApps"] = null,
                                        ["plApps"] = null,
                                    },
                                    ["version"] = "0.0.1",
                                }
                            },
                            ["personaProfile3"] = new Dictionary<string, object>
                            {
                                ["ObjectName"] = "personaProfile3",
                                ["DataObject"] = new Dictionary<string, object>
                                {
                                    ["personaCollection"] = new Dictionary<string, object>
                                    {
                                        ["universalApp"] = new object[]
                                        {
                                new Dictionary<string, object>
                                {
                                    ["arm"] = "wide",
                                    ["skcol"] = "#ffb37b62",
                                    ["skin"] = false,
                                },
                                new Dictionary<string, object> { ["id"] = "", },
                                new Dictionary<string, object> { ["id"] = "8f96d1f8-e9bb-40d2-acc8-eb79746c5d7c/d", },
                                new Dictionary<string, object> { ["id"] = "1042557f-d1f9-44e3-ba78-f404e8fb7363/d", },
                                new Dictionary<string, object> { ["id"] = "f1e4c577-19ba-4d77-9222-47f145857f78/d", },
                                new Dictionary<string, object> { ["id"] = "49f93789-a512-4c47-95cb-0606cdc1c2be/d", },
                                new Dictionary<string, object> { ["id"] = "68bfe60d-f30a-422f-b32c-72374ebdd057/d" ,},
                                new Dictionary<string, object> { ["id"] = "b6702f0e-a4b5-497a-8820-6c8e3946bb55/d", },
                                new Dictionary<string, object>
                                {
                                    ["col"] = new object[] { "#0", "#0", "#ff774235", "#0" },
                                    ["id"] = "52dd0726-cd68-4d7d-8561-515a4866de39/d",
                                },
                                new Dictionary<string, object>
                                {
                                    ["col"] = new object[] { "#ff523d89", "#0", "#0", "#0" },
                                    ["id"] = "a0f263b3-e093-4c85-aadb-3759417898ff/d",
                                },
                                new Dictionary<string, object>
                                {
                                    ["col"] = new object[] { "#ff2f1f0f", "#0", "#0", "#0" },
                                    ["id"] = "2bb1473b-9a5c-4eae-9fd5-82302a6aa3da/d",
                                }
                                        }
                                    },
                                    ["version"] = "0.0.1",
                                }
                            },
                            ["personaProfile4"] = new Dictionary<string, object>
                            {
                                ["ObjectName"] = "personaProfile4",
                                ["DataObject"] = new Dictionary<string, object>
                                {
                                    ["personaCollection"] = new Dictionary<string, object>
                                    {
                                        ["universalApp"] = new object[]
                                        {
                                new Dictionary<string, object>
                                {
                                    ["arm"] = "slim",
                                    ["skcol"] = "#fff2dbbd",
                                    ["skin"] = true,
                                },
                                new Dictionary<string, object> { ["id"] = "8f96d1f8-e9bb-40d2-acc8-eb79746c5d7c/d", },
                                new Dictionary<string, object> { ["id"] = "1042557f-d1f9-44e3-ba78-f404e8fb7363/d", },
                                new Dictionary<string, object> { ["id"] = "0948e089-6f9c-40c1-886b-cd37add03f69/d", },
                                new Dictionary<string, object> { ["id"] = "96db6e5b-dc69-4ebc-bd36-cb1b08ffb0f4/d", },
                                new Dictionary<string, object> { ["id"] = "5f64b737-b88a-40ea-be1f-559840237146/d" ,},
                                new Dictionary<string, object>
                                {
                                    ["col"] = new object[] { "#0", "#0", "#ffefbbb1", "#0" },
                                    ["id"] = "83c940ce-d7b8-4603-8d73-c1234e322cce/d",
                                },
                                new Dictionary<string, object>
                                {
                                    ["col"] = new object[] { "#ff236224", "#0", "#0", "#0" },
                                    ["id"] = "a0f263b3-e093-4c85-aadb-3759417898ff/d",
                                },
                                new Dictionary<string, object>
                                {
                                    ["col"] = new object[] { "#ffe89d4c", "#0", "#0", "#0" },
                                    ["id"] = "70be0801-a93f-4ce0-8e3f-7fdeac1e03b9/d",
                                },
                                new Dictionary<string, object> { ["id"] = "80eda582-cda7-4fce-9d6f-89a60f2448f1/d", }
                                        }
                                    },
                                    ["version"] = "0.0.1",
                                }
                            }
                        },
                        ["Entity"] = new Dictionary<string, object>
                        {
                            ["Id"] = request.Entity.Id,
                            ["Type"] = request.Entity.Type,
                            ["TypeString"] = request.Entity.Type,
                        },
                    }
                )),// TODO:
            _ => TypedResults.BadRequest(),
        };
    }

    private sealed record SetObjectsRequest(
        SetObjectsRequest.EntityR Entity,
        object? Objects
    )
    {
        public sealed record EntityR(
            string Id,
            string Type
        );

        public sealed record ObjectsR(
        // TODO  
        );
    }

    [HttpPost("SetObjects")]
    public async Task<Results<Ok, ForbidHttpResult, BadRequest>> SetObjects()
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        var request = await Request.Body.AsJsonAsync<SetObjectsRequest>(cancellationToken);

        if (request is null)
        {
            return TypedResults.BadRequest();
        }

        var tokenUnion = PlayfabAuth();

        if (tokenUnion.IsB)
        {
            return tokenUnion.B.Result is ForbidHttpResult forbid ? forbid : (BadRequest)tokenUnion.B.Result;
        }

        var token = tokenUnion.A;

        if (token.Id != request.Entity.Id || token.Type != request.Entity.Type)
        {
            return TypedResults.Forbid();
        }

        return request.Entity.Type switch
        {
            "master_player_account" => TypedResults.Ok(),// TODO
            _ => TypedResults.BadRequest(),
        };
    }
}
