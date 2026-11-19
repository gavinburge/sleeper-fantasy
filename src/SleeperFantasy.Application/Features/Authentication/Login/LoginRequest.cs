namespace SleeperFantasy.Application.Features.Authentication.Login;

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);
