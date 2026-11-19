using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;

namespace SleeperFantasy.Application.Features.Authentication.Login;

public static class LoginEndpoint
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext context,
            IAntiforgery antiforgery,
            IAdministratorSession session,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "A valid antiforgery token is required." });
            }

            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return Results.Unauthorized();
            }

            var signedIn = await session.SignInAsync(
                request.Email,
                request.Password,
                request.RememberMe,
                cancellationToken);

            return signedIn ? Results.Ok() : Results.Unauthorized();
        });
    }
}
