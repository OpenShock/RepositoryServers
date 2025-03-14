using System.Text.Json;
using Asp.Versioning;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenShock.Desktop.RepositoryServer;
using OpenShock.Desktop.RepositoryServer.AuthenticationHandlers;
using OpenShock.Desktop.RepositoryServer.Config;
using OpenShock.Desktop.RepositoryServer.ExceptionHandler;
using OpenShock.Desktop.RepositoryServer.RepoServerDb;
using OpenShock.Desktop.RepositoryServer.Utils;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;
using Serilog;
using ValidationProblem = OpenShock.Desktop.RepositoryServer.Problems.ValidationProblem;

var builder = WebApplication.CreateSlimBuilder(args);
        
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Custom.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>(true)
    .AddCommandLine(args);

var isDevelopment = builder.Environment.IsDevelopment();
builder.Host.UseDefaultServiceProvider((_, options) =>
{
    options.ValidateScopes = isDevelopment;
    options.ValidateOnBuild = isDevelopment;
});

// Since we use slim builders, this allows for HTTPS during local development
if (isDevelopment) builder.WebHost.UseKestrelHttpsConfiguration();
        
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(80, options =>
    {
        options.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
    });
#if DEBUG
    serverOptions.ListenAnyIP(443, options =>
    {
        options.UseHttps();
        options.Protocols = HttpProtocols.Http1AndHttp2AndHttp3;
    });
#endif
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMilliseconds(3000);
});

builder.Host.UseSerilog((context, _, config) => config.ReadFrom.Configuration(context.Configuration));

var config = builder.GetAndRegisterOpenShockConfig<ApiConfig>();

// <---- ASP.NET ---->
builder.Services.AddExceptionHandler<OpenShockExceptionHandler>();

builder.Services.AddAuthenticationCore();
new AuthenticationBuilder(builder.Services)
    .AddScheme<AuthenticationSchemeOptions, AdminTokenAuthentication>(
        AuthSchemas.AdminToken, _ => { });

builder.Services.AddAuthorization();


builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new SemVersionConverter());
});

builder.Services.AddControllers().AddJsonOptions(x =>
{
    x.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    x.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    x.JsonSerializerOptions.Converters.Add(new SemVersionConverter());
});

var apiVersioningBuilder = builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
});

apiVersioningBuilder.AddApiExplorer(setup =>
{
    setup.GroupNameFormat = "VVV";
    setup.SubstituteApiVersionInUrl = true;
    setup.DefaultApiVersion = new ApiVersion(1, 0);
    setup.AssumeDefaultVersionWhenUnspecified = true;
});

// generic ASP.NET stuff
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddWebEncoders();
builder.Services.AddProblemDetails();
builder.Services.TryAddSingleton<TimeProvider>(provider => TimeProvider.System);

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(corsPolicyBuilder =>
    {
        corsPolicyBuilder.SetIsOriginAllowed(s => true);
        corsPolicyBuilder.AllowAnyHeader();
        corsPolicyBuilder.AllowCredentials();
        corsPolicyBuilder.AllowAnyMethod();
        corsPolicyBuilder.SetPreflightMaxAge(TimeSpan.FromHours(24));
    });
});

// This needs to be at this position, earlier will break validation error responses
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problemDetails = new ValidationProblem(context.ModelState);
        return problemDetails.ToObjectResult(context.HttpContext);
    };
});

// OpenTelemetry

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddRuntimeInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter());

// <---- Postgres EF Core ---->
        
builder.Services.AddDbContextPool<RepoServerContext>(dbBuilder =>
{
    RepoServerContext.ConfigureOptionsBuilder(dbBuilder, config.Db.Conn, config.Db.Debug);
});

builder.Services.AddPooledDbContextFactory<RepoServerContext>(dbBuilder =>
{
    dbBuilder.UseNpgsql(config.Db.Conn);
    dbBuilder.UseExceptionProcessor();
    if (config.Db.Debug)
    {
        dbBuilder.EnableSensitiveDataLogging();
        dbBuilder.EnableDetailedErrors();
    }
});



var app = builder.Build();

if (!config.Db.SkipMigration)
{
    Log.Information("Running database migrations...");
    using var scope = app.Services.CreateScope();
    
    await using var migrationContext = new MigrationOpenShockContext(
        config.Db.Conn,
        config.Db.Debug, 
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
    var pendingMigrations = migrationContext.Database.GetPendingMigrations().ToArray();

    if (pendingMigrations.Length > 0)
    {
        Log.Information("Found pending migrations, applying [{@Migrations}]", pendingMigrations);
        migrationContext.Database.Migrate();
        Log.Information("Applied database migrations... proceeding with startup");
    }
    else
    {
        Log.Information("No pending migrations found, proceeding with startup");
    }
}
else
{
    Log.Warning("Skipping possible database migrations...");
}

app.UseSerilogRequestLogging();

// Enable request body buffering. Needed to allow rewinding the body reader,
// if the body has already been read before.
// Runs before the request action is executed and body is read.
app.Use((context, next) =>
{
    context.Request.EnableBuffering();
    return next.Invoke();
});
app.UseExceptionHandler();

// global cors policy
app.UseCors();
        
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromMinutes(1)
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

var metricsAllowedIpNetworks = config.Metrics.AllowedNetworks.Select(x => IPNetwork.Parse(x));
        
app.UseOpenTelemetryPrometheusScrapingEndpoint(context =>
{
    if(context.Request.Path != "/metrics") return false;
            
    var remoteIp = context.Connection.RemoteIpAddress;
    return remoteIp != null && metricsAllowedIpNetworks.Any(x => x.Contains(remoteIp));
});

app.MapOpenApi();
app.MapControllers();

app.MapScalarApiReference();

app.Run();
