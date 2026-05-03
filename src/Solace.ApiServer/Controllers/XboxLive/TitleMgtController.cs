using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Solace.ApiServer.Controllers.XboxLive;

[Route("titles")]
[Route("title.mgt.xboxlive.com/titles")]
internal sealed class TitleMgtController : SolaceControllerBase
{
    private static readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static DomainParser? _domainParser;

    private sealed record EndpointsResponse(IEnumerable<Endpoint> EndPoints);

    private sealed record Endpoint(string Protocol, string Host, int? Port, string HostType, string? RelyingParty, string? TokenType);

    [HttpGet("{title}/endpoints")]
    public async Task<Results<ContentHttpResult, BadRequest>> GetEndpoints(string title)
    {
        var cancellationToken = Request.HttpContext.RequestAborted;

        IEnumerable<Endpoint> endpoints;

        switch (title)
        {
            case "default":
                {
                    string protocol = Request.IsHttps ? "https" : "http";
                    var host = Request.Host;
                    Debug.Assert(host.HasValue);

                    bool isHostIp = IPAddress.TryParse(host.Host, out _);

                    bool singleDomainMode = !Request.Path.StartsWithSegments(new PathString("/titles"), StringComparison.Ordinal);

                    string hostString = isHostIp
                        ? host.Host
                        : (await GetDomainParserAsync(cancellationToken)).Parse(host.Host)?.RegistrableDomain ?? host.Host;

                    endpoints =
                    [
                        singleDomainMode ?
                        new Endpoint(
                            protocol,
                            hostString,
                            host.Port ?? 80,
                            isHostIp ? "ip" : "fqdn",
                            "http://xboxlive.com",
                            "JWT"
                        ) :
                        new Endpoint(
                            protocol,
                            $"*.{hostString}",
                            host.Port ?? 80,
                            "wildcard",
                            "http://xboxlive.com",
                            "JWT"
                        ),
                        new Endpoint(
                            "https",
                            "xboxlive.com",
                            null,
                            "fqdn",
                            "http://xboxlive.com",
                            "JWT"
                        ),
                    ];
                }

                break;
            case "2037747551":
                {
                    endpoints =
                    [
                        new Endpoint(
                            "https",
                            "*.playfabapi.com",
                            null,
                            "wildcard",
                            "https://b980a380.minecraft.playfabapi.com/",
                            "JWT"
                        ),
                        new Endpoint(
                            "https",
                            "*.commerce.gameservices.com",
                            null,
                            "wildcard",
                            "https://minecraft.commerce.microsoftstudios.com/",
                            "JWT"
                        ),
                        new Endpoint(
                            "http",
                            "*",
                            null,
                            "wildcard",
                            null,
                            null
                        ),
                        new Endpoint(
                            "https",
                            "*",
                            null,
                            "wildcard",
                            null,
                            null
                        ),
                    ];
                }

                break;
            default:
                return TypedResults.BadRequest();
        }

        return TypedResults.Content(JsonSerializer.Serialize(new EndpointsResponse(endpoints), jsonOptions), "application/json");
    }

    private static async Task<DomainParser> GetDomainParserAsync(CancellationToken cancellationToken)
    {
        if (_domainParser is not null)
        {
            return _domainParser;
        }

        var ruleProvider = new LocalFileRuleProvider("public_suffix_list.dat");
        await ruleProvider.BuildAsync(cancellationToken: cancellationToken);

        return _domainParser = new DomainParser(ruleProvider);
    }
}
