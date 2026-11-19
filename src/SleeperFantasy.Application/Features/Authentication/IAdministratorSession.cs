namespace SleeperFantasy.Application.Features.Authentication;

public interface IAdministratorSession
{
    Task<bool> SignInAsync(
        string email,
        string password,
        bool isPersistent,
        CancellationToken cancellationToken);

    Task SignOutAsync();
}
