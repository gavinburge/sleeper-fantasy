using Microsoft.AspNetCore.Routing;

namespace SleeperFantasy.Application.Features.Authentication.Session;

public static class SessionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/session", (HttpContext context) =>
                Results.Ok(new { authenticated = context.User.Identity?.IsAuthenticated == true }));
    }
}
