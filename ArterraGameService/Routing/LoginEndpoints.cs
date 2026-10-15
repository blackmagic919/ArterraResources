using Arterra.Game.Handlers;
using Arterra.Game.Schemas;

namespace Arterra.Game.Routing;

public static class LoginEndpoints {
    public static void MapLogin(this IEndpointRouteBuilder endpoints) {
        endpoints.MapPost("/login", async (LoginRequest request, LoginService login, HttpContext context) => {
            context.Response.Headers.CacheControl = "no-store";
            try {
                return Results.Ok(await login.LoginAsync(request, context.RequestAborted));
            } catch (ArgumentException exception) {
                return Results.BadRequest(new { error = exception.Message });
            } catch (UnauthorizedAccessException) {
                return Results.Unauthorized();
            }
        });
    }
}
