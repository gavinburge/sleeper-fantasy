using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using SleeperFantasy.Application.Abstractions;
using SleeperFantasy.Domain.Analysis;
using SleeperFantasy.Domain.Entities;

namespace SleeperFantasy.Application.Features.Analysis;

public static class AnalysisEndpointMapping
{
    public static IEndpointRouteBuilder MapAnalysisEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var analysis = endpoints.MapGroup("/api/v1/analysis")
            .RequireAuthorization()
            .WithTags("Analysis");

        analysis.MapPost("/run", async (
            HttpContext context,
            IAntiforgery antiforgery,
            ClaimsPrincipal user,
            IAppDbContext dbContext,
            AnalysisRunService runService,
            IAnalysisExecutionTrigger trigger,
            CancellationToken cancellationToken) =>
        {
            if (!await ValidateAntiforgeryAsync(context, antiforgery))
            {
                return Results.BadRequest(new
                {
                    error = "ANTIFORGERY_REQUIRED",
                    message = "A valid antiforgery token is required."
                });
            }

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var configuration = await dbContext.ManagerConfigurations
                .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
            
            if (configuration is null)
            {
                return Results.Conflict(new
                {
                    error = "CONFIGURATION_REQUIRED",
                    message = "Configure a league before starting analysis."
                });
            }

            var run = await runService.CreateOrReuseAsync(configuration, cancellationToken);
            
            var executionId = await trigger.TriggerAsync(run.Id, cancellationToken);
            
            if (executionId is not null)
            {
                run.CloudRunExecutionId = executionId;
                run.Stage = "submitted";
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            
            return Results.Accepted($"/api/v1/analysis/latest", AnalysisRunService.ToResponse(run));
        })
            .WithName("StartAnalysis")
            .Produces<AnalysisRunResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        analysis.MapGet("/latest", async (
            ClaimsPrincipal user,
            IAppDbContext dbContext,
            AnalysisRunService runService,
            CancellationToken cancellationToken) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var configurationId = await dbContext.ManagerConfigurations
                .Where(x => x.UserId == userId)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
            
            if (configurationId is null)
            {
                return Results.NotFound(new { error = "CONFIGURATION_NOT_FOUND" });
            }

            var latest = await runService.GetLatestAsync(configurationId.Value, cancellationToken);
            
            return latest is null
                ? Results.NotFound(new { error = "ANALYSIS_NOT_FOUND" })
                : Results.Ok(latest);
        })
            .WithName("GetLatestAnalysis")
            .Produces<AnalysisLatestResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        analysis.MapPost("/{runId:guid}/retry", async (
            Guid runId,
            HttpContext context,
            IAntiforgery antiforgery,
            ClaimsPrincipal user,
            IAppDbContext dbContext,
            AnalysisRunService runService,
            IAnalysisExecutionTrigger trigger,
            CancellationToken cancellationToken) =>
        {
            if (!await ValidateAntiforgeryAsync(context, antiforgery))
            {
                return Results.BadRequest(new
                {
                    error = "ANTIFORGERY_REQUIRED",
                    message = "A valid antiforgery token is required."
                });
            }

            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var run = await dbContext.AnalysisRuns
                .Include(x => x.ManagerConfiguration)
                .SingleOrDefaultAsync(
                    x => x.Id == runId && x.ManagerConfiguration!.UserId == userId,
                    cancellationToken);
            
            if (run is null)
            {
                return Results.NotFound(new { error = "ANALYSIS_NOT_FOUND" });
            }

            AnalysisRun retry;
            
            try
            {
                retry = await runService.CreateRetryAsync(run, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new
                {
                    error = "RETRY_NOT_AVAILABLE",
                    message = exception.Message
                });
            }

            var executionId = await trigger.TriggerAsync(retry.Id, cancellationToken);
            
            if (executionId is not null)
            {
                retry.CloudRunExecutionId = executionId;
                retry.Stage = "submitted";
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            
            return Results.Accepted("/api/v1/analysis/latest", AnalysisRunService.ToResponse(retry));
        })
            .WithName("RetryAnalysis")
            .Produces<AnalysisRunResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<bool> ValidateAntiforgeryAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
