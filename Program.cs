using System.Net;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

var app = builder.Build();

var apiAppUrl = builder.Configuration["ApiApp:Url"] ?? "https://m1.caservices.visiondsm.com/system/";

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/context", (HttpContext context) =>
{
    var hostInfo = DescribeHost(context.Request.Host.Host);

    return Results.Json(new
    {
        message = hostInfo.Message,
        host = context.Request.Host.Host,
        accessMode = hostInfo.AccessMode,
        accessValue = hostInfo.AccessValue,
        renderedAtUtc = DateTimeOffset.UtcNow.ToString("u")
    });
});

app.MapGet("/api/data", async (IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
{
    var client = httpClientFactory.CreateClient();
    client.Timeout = TimeSpan.FromSeconds(20);

    try
    {
        using var response = await client.GetAsync(apiAppUrl, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Results.Json(
                new
                {
                    ok = false,
                    statusCode = (int)response.StatusCode,
                    reasonPhrase = response.ReasonPhrase ?? "Unknown error",
                    data = responseText
                },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Json(new
        {
            ok = true,
            statusCode = (int)response.StatusCode,
            data = responseText
        });
    }
    catch (HttpRequestException exception)
    {
        return Results.Json(
            new
            {
                ok = false,
                statusCode = (int)HttpStatusCode.BadGateway,
                reasonPhrase = "Request to API App failed",
                data = exception.Message
            },
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException)
    {
        return Results.Json(
            new
            {
                ok = false,
                statusCode = (int)HttpStatusCode.GatewayTimeout,
                reasonPhrase = "Request to API App timed out",
                data = "The request to API App did not complete in time."
            },
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapFallbackToFile("index.html");

app.Run();

static HostInfo DescribeHost(string host)
{
    if (Regex.IsMatch(host, @"^portals[^.]*\.ca\.cloud\.visiondsm\.com$", RegexOptions.IgnoreCase))
    {
        return new HostInfo(
            "root",
            "root domain",
            "This is Portals App. You are accessing via root domain.");
    }

    var wildcardMatch = Regex.Match(
        host,
        @"^(?<subdomain>[^.]+)\.(?<root>portals[^.]*\.ca\.cloud\.visiondsm\.com)$",
        RegexOptions.IgnoreCase);

    if (wildcardMatch.Success)
    {
        var wildcardSubdomain = wildcardMatch.Groups["subdomain"].Value;

        return new HostInfo(
            "wildcard",
            wildcardSubdomain,
            $"This is Portals App. You are accessing via {wildcardSubdomain}.");
    }

    return new HostInfo(
        "direct",
        host,
        $"This is Portals App. You are accessing via direct hostname {host}.");
}

internal sealed record HostInfo(string AccessMode, string AccessValue, string Message);
