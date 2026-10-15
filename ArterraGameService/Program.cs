using Arterra.Game.Routing;
using Arterra.Game.Authentication;
using Arterra.Game.Handlers;
using Arterra.Game.Providers;
using Arterra.Game.Schemas;
using Arterra.Game.Storage;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
builder.Services.AddHttpClient("Unity", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IIdentityProvider>(services => new UnityIdentityProvider(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("Unity"), Required("UNITY_PROJECT_ID")));
builder.Services.AddSingleton<IIdentityStore>(services => new DynamoIdentityStore(
    services.GetRequiredService<IAmazonDynamoDB>(), Required("IDENTITY_TABLE")));
builder.Services.AddSingleton<ILoginTokenStore>(services => new DynamoLoginTokenStore(
    services.GetRequiredService<IAmazonDynamoDB>(), Required("LOGIN_TABLE")));
builder.Services.AddSingleton<LoginService>();
builder.Services.AddSingleton<ISessionStore>(services => new DynamoSessionStore(
    services.GetRequiredService<IAmazonDynamoDB>(), Required("SESSION_TABLE"), Required("METADATA_BUCKET"), services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<ISessionMetadata>(services => new S3SessionMetadata(
    services.GetRequiredService<IAmazonS3>(), Required("METADATA_BUCKET")));
builder.Services.AddSingleton(new NetworkEndpoint(Required("CONNECTIVITY_HOST"), int.Parse(Required("CONNECTIVITY_PORT"))));
builder.Services.AddSingleton<IConnectionTicketStore>(services => new DynamoConnectionTicketStore(
    services.GetRequiredService<IAmazonDynamoDB>(), Required("TICKET_TABLE"), services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<SessionService>();
builder.Services.AddAuthentication(LoginAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, LoginAuthenticationHandler>(LoginAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapLogin();
app.MapSessions();
app.Run();

string Required(string key) => builder.Configuration[key]
    ?? throw new InvalidOperationException($"Missing required setting {key}.");
