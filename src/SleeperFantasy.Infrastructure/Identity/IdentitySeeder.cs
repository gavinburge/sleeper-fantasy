using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SleeperFantasy.Infrastructure.Configuration;

namespace SleeperFantasy.Infrastructure.Identity;

public static class IdentitySeeder
{
    public static async Task SeedAdministratorAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<Configuration.IdentityOptions>>().Value;
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var existingUser = await userManager.FindByEmailAsync(options.AdministratorEmail);
        if (existingUser is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = options.AdministratorEmail,
            Email = options.AdministratorEmail,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, options.AdministratorPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Unable to create the administrator account: {errors}");
        }
    }
}
