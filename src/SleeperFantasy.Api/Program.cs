using SleeperFantasy.Application.Features.Authentication;
using SleeperFantasy.Infrastructure;
using SleeperFantasy.Infrastructure.Identity;
using SleeperFantasy.Infrastructure.Persistence;
using SleeperFantasy.Application.Features.Analysis;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAntiforgery();
builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    var webOrigin = builder.Configuration["Runtime:PublicBaseUrl"] ?? "http://localhost:5187";
    var allowedOrigins = new List<string> { webOrigin };
    if (builder.Environment.IsDevelopment())
    {
        allowedOrigins.AddRange([
            "http://localhost:5055",
            "https://localhost:7040",
            "https://localhost:5055"
        ]);
    }

    options.AddPolicy("WebClient", policy =>
        policy.WithOrigins(allowedOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.ApplyMigrationsAsync();
await app.Services.SeedAdministratorAsync();

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("WebClient");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthenticationEndpoints();
app.MapAnalysisEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.Run();

public partial class Program
{
}
