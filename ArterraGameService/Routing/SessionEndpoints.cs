using Arterra.Game.Handlers;
using Arterra.Game.Schemas;
using System.Security.Claims;

namespace Arterra.Game.Routing;

public static class SessionEndpoints {
    public static void MapSessions(this IEndpointRouteBuilder endpoints) {
        var group = endpoints.MapGroup("/sessions").AddEndpointFilter(async (context, next) => {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (SessionRequestException exception) {
                return Results.Json(new { error = exception.Message }, statusCode: exception.StatusCode);
            }
        });

        group.MapPost("/", async (CreateSessionRequest request, SessionService service, HttpContext context) => {
            var result = await service.CreateAsync(UserId(context), request, context.RequestAborted);
            return Results.Created($"/sessions/{result.SessionId}", result);
        }).RequireAuthorization();

        group.MapPost("/{id:guid}/complete", async (Guid id, SessionService service, HttpContext context) => {
            await service.CompleteAsync(id, UserId(context), context.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization();

        group.MapGet("/", async (int? limit, Guid? cursor, SessionService service, CancellationToken cancel) =>
            Results.Ok(await service.ListAsync(limit ?? 20, cursor, cancel)));

        group.MapGet("/{id:guid}", async (Guid id, SessionService service, CancellationToken cancel) =>
            Results.Ok(await service.GetActiveAsync(id, cancel)));

        group.MapPost("/{id:guid}/host", async (Guid id, SessionService service, HttpContext context) =>
            Results.Ok(await service.HostAsync(id, UserId(context), context.RequestAborted))).RequireAuthorization();

        group.MapPost("/{id:guid}/join", async (Guid id, SessionService service, HttpContext context) =>
            Results.Ok(await service.JoinAsync(id, UserId(context), context.RequestAborted))).RequireAuthorization();

        group.MapDelete("/{id:guid}", async (Guid id, SessionService service, HttpContext context) => {
            await service.DeleteAsync(id, UserId(context), context.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization();
    }

    private static Guid UserId(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
