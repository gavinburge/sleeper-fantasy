using Microsoft.AspNetCore.Routing;
using SleeperFantasy.Application.Features.Authentication.Login;
using SleeperFantasy.Application.Features.Authentication.Logout;
using SleeperFantasy.Application.Features.Authentication.Session;

namespace SleeperFantasy.Application.Features.Authentication;

public static class AuthenticationEndpointMapping
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var authentication = endpoints.MapGroup("/api/auth")
            .WithTags("Authentication");

        AntiforgeryEndpoint.Map(authentication);
        LoginEndpoint.Map(authentication);
        LogoutEndpoint.Map(authentication);
        SessionEndpoint.Map(authentication);

        return endpoints;
    }
}
