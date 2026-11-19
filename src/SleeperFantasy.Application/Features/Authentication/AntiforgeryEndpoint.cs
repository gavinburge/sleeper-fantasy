using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;

namespace SleeperFantasy.Application.Features.Authentication;

public static class AntiforgeryEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });
    }
}
