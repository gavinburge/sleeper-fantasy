using Microsoft.AspNetCore.Identity;
using SleeperFantasy.Application.Features.Authentication;

namespace SleeperFantasy.Infrastructure.Identity;

internal sealed class IdentityAdministratorSession(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : IAdministratorSession
{
    public async Task<bool> SignInAsync(
        string email,
        string password,
        bool isPersistent,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return false;
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            password,
            isPersistent,
            lockoutOnFailure: true);

        return result.Succeeded;
    }

    public Task SignOutAsync() => signInManager.SignOutAsync();
}
