using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Solace.ApiServer.Controllers.XboxAb;

[Route("ab")]
[Route("www.xboxab.com/ab")]
internal sealed class AbController : SolaceControllerBase
{
    // TODO: try to set sunsetting to 0/false, see what it does
    [HttpGet]
    public ContentHttpResult Get()
#pragma warning disable CA1825 // Avoid zero-length array allocations
#pragma warning disable CA1861 // Avoid constant arrays as arguments
        => JsonPascalCase(new Dictionary<string, object?>()
        {
            ["Features"] = new string[]
            {
                //"mc-sunsetting_1",
                //"mc-reco-algo2simfirst",
                //"mc-rp-hero-row-timer-2",
                //"mc-signaling-usewebsockets",
                //"mc-signaling-useturn",
                //"mcmktvlt-offerids-recos_lgbm3c",
                //"mc-cloud-file-upload",
                //"mc-oneds-prod",
                //"mc-realms-cayman",
                //"mc-realms-libhttp",
                //"mc-rp-morelicensedsidebar",
                //"control-tower-says-yes",
                //"raknet-enabled",
                //"mc-rp-nycbanner3",
                //"mc-rp-risinglava",
            },
            ["Flights"] = new Dictionary<string, string>()
            {
                //["28kk"] = "mc-sunsetting_1",
                //["2mky"] = "mc-reco-algo2simfirst",
                //["2qco"] = "mc-rp-hero-row-timer-2",
                //["2x69"] = "mc-signaling-usewebsockets",
                //["2x6m"] = "mc-signaling-useturn",
                //["3gfd"] = "mcmktvlt-offerids-recos_lgbm3c",
                //["3gth"] = "mc-cloud-file-upload",
                //["3gw8"] = "mc-oneds-prod",
                //["3iu5"] = "mc-realms-cayman",
                //["3ol8"] = "mc-realms-libhttp",
                //["4geb"] = "mc-rp-morelicensedsidebar",
                //["4j7l"] = "control-tower-says-yes",
                //["4o5r"] = "raknet-enabled",
                //["4p1g"] = "mc-rp-nycbanner3",
                //["4pan"] = "mc-rp-risinglava",
            },
            ["Configs"] = new object[]
            {
                //new Dictionary<string, object>()
                //{
                //    ["Id"] = "Minecraft",
                //    ["Parameters"] = new Dictionary<string, object>()
                //    {
                //        ["sunsetting"] = true,
                //        ["algo"] = "two",
                //        ["fjkdsafjlkdsafdjlk"] = true,
                //        ["mc-signaling-usewebsockets"] = true,
                //        ["mc-signaling-useturn"]= true,
                //        ["lgbm3c"] = true,
                //        ["mc-cloud-file-upload"] = true,
                //        ["mc-oneds-prod"] = true,
                //        ["ennables_realms_cayman_sub"] = true,
                //        ["mc-realms-libhttp-treatment-032922"] = true,
                //        ["dfasdfsfd"] = true,
                //        ["mc-control-tower-says-yes"] = true,
                //        ["mc-bedrock-host-raknet"] = true,
                //        ["nyc3banner132023"] = true,
                //        ["withbuttonart132023"] = true,
                //    }
                //}
            },
            ["ParameterGroups"] = null,//Array.Empty<object>(),
            ["FlightingVersion"] = 53535138,
            ["ImpressionId"] = "CE0898A92D6A4755A37D4F4561983AFE",
            ["AssignmentContext"] = "", //"mc-sunsetting_1:30259009;mc-reco-algo2simfirst:30617967;mc-rp-hero-row-timer-2:30321236;mc-signaling-usewebsockets:30418088;mc-signaling-useturn:30357350;mcmktvlt-offerids-recos_lgbm3c:30622925;mc-cloud-file-upload:30440330;mc-oneds-prod:30450267;mc-realms-cayman:30606215;mc-realms-libhttp:30526657;mc-rp-morelicensedsidebar:30594955;control-tower-says-yes:30600225;raknet-enabled:30640316;mc-rp-nycbanner3:30636461;mc-rp-risinglava:30636462;",
        });
#pragma warning restore CA1861 // Avoid constant arrays as arguments
#pragma warning restore CA1825 // Avoid zero-length array allocations
}
