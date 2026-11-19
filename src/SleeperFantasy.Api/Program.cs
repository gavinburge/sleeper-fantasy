using SleeperFantasy.Application.Features.Authentication;
using SleeperFantasy.Infrastructure;
using SleeperFantasy.Infrastructure.Identity;
using SleeperFantasy.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAntiforgery();
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();

await app.Services.ApplyMigrationsAsync();
await app.Services.SeedAdministratorAsync();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthenticationEndpoints();

app.Run();

public partial class Program
{
}
