using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;

namespace SleeperFantasy.Application.Features.Authentication.Logout;

public static class LogoutEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/logout", async (
            HttpContext context,
            IAntiforgery antiforgery,
            IAdministratorSession session) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "A valid antiforgery token is required." });
            }

            await session.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
