using Microsoft.AspNetCore.Routing;

namespace SleeperFantasy.Application.Features.Authentication.Session;

public static class SessionEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/session", (HttpContext context) =>
                Results.Ok(new { authenticated = context.User.Identity?.IsAuthenticated == true }))
            .WithName("GetSession")
            .WithSummary("Check the current session")
            .WithDescription("Reports whether the current request has a valid application session.")
            .Produces(StatusCodes.Status200OK);
    }
}
