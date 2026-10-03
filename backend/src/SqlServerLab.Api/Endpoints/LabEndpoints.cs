using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SqlServerLab.Api.Auth;
using SqlServerLab.Application.Audit;
using SqlServerLab.Application.Jobs;
using SqlServerLab.Application.Labs;

namespace SqlServerLab.Api.Endpoints;

public static class LabEndpoints
{
    public const string MutationRateLimit = "mutations";
    private const string IdempotencyHeader = "Idempotency-Key";

    public static RouteGroupBuilder MapLabEndpoints(this RouteGroupBuilder api)
    {
        var labs = api.MapGroup("/labs").WithTags("Labs").RequireAuthorization(LabPolicies.User);

        labs.MapGet("/", async Task<Ok<IReadOnlyList<LabDto>>> (LabService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAsync(ct)))
            .WithName("ListLabs");

        labs.MapPost("/", async Task<Accepted<CreateLabResponse>> (
                CreateLabRequest request,
                [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                LabService service,
                CancellationToken ct) =>
            {
                var result = await service.CreateAsync(request, idempotencyKey, ct);
                return TypedResults.Accepted(result.Job.StatusUrl, result);
            })
            .WithName("CreateLab")
            .RequireRateLimiting(MutationRateLimit)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        labs.MapGet("/{labId:guid}", async Task<Ok<LabDto>> (Guid labId, LabService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetAsync(labId, ct)))
            .WithName("GetLab")
            .ProducesProblem(StatusCodes.Status404NotFound);

        labs.MapPost("/{labId:guid}/start", async Task<Accepted<JobDto>> (
                Guid labId, [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, LabService service, CancellationToken ct) =>
            Accepted(await service.StartAsync(labId, idempotencyKey, ct)))
            .WithName("StartLab")
            .RequireRateLimiting(MutationRateLimit)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        labs.MapPost("/{labId:guid}/deallocate", async Task<Accepted<JobDto>> (
                Guid labId, [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey, LabService service, CancellationToken ct) =>
            Accepted(await service.DeallocateAsync(labId, idempotencyKey, ct)))
            .WithName("DeallocateLab")
            .RequireRateLimiting(MutationRateLimit)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        labs.MapPost("/{labId:guid}/extend-expiration", async Task<Ok<LabDto>> (
                Guid labId, ExtendExpirationRequest request, LabService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ExtendExpirationAsync(labId, request, ct)))
            .WithName("ExtendLabExpiration")
            .RequireRateLimiting(MutationRateLimit)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        labs.MapDelete("/{labId:guid}", async Task<Accepted<JobDto>> (
                Guid labId,
                [FromBody] DeleteLabRequest request,
                [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
                LabService service,
                CancellationToken ct) =>
            Accepted(await service.DeleteAsync(labId, request, idempotencyKey, ct)))
            .WithName("DeleteLab")
            .RequireRateLimiting(MutationRateLimit)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        labs.MapGet("/{labId:guid}/jobs", async Task<Ok<IReadOnlyList<JobDto>>> (Guid labId, LabService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListJobsAsync(labId, ct)))
            .WithName("ListLabJobs")
            .ProducesProblem(StatusCodes.Status404NotFound);

        labs.MapGet("/{labId:guid}/audit", async Task<Ok<IReadOnlyList<AuditEventDto>>> (Guid labId, LabService service, CancellationToken ct) =>
            TypedResults.Ok(await service.ListAuditAsync(labId, ct)))
            .WithName("ListLabAudit")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return api;
    }

    public static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder api)
    {
        var jobs = api.MapGroup("/jobs").WithTags("Jobs").RequireAuthorization(LabPolicies.User);

        jobs.MapGet("/{jobId:guid}", async Task<Ok<JobDto>> (Guid jobId, LabService service, CancellationToken ct) =>
            TypedResults.Ok(await service.GetJobAsync(jobId, ct)))
            .WithName("GetJob")
            .ProducesProblem(StatusCodes.Status404NotFound);

        jobs.MapPost("/{jobId:guid}/cancel", async Task<Accepted<JobDto>> (Guid jobId, LabService service, CancellationToken ct) =>
            Accepted(await service.CancelJobAsync(jobId, ct)))
            .WithName("CancelJob")
            .RequireRateLimiting(MutationRateLimit)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }

    public static RouteGroupBuilder MapEnvironmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/environment", ([FromServices] EnvironmentDto environment) => TypedResults.Ok(environment))
            .WithTags("Environment")
            .WithName("GetEnvironment")
            .RequireAuthorization(LabPolicies.User);
        return api;
    }

    private static Accepted<JobDto> Accepted(JobDto job) => TypedResults.Accepted(job.StatusUrl, job);
}
