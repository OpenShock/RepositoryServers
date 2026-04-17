using System.Net;
using System.Text.Json;
using Asp.Versioning;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenShock.RepositoryServer;
using OpenShock.RepositoryServer.AuthenticationHandlers;
using OpenShock.RepositoryServer.Config;
using OpenShock.RepositoryServer.ExceptionHandler;
using OpenShock.RepositoryServer.RepoServerDb;
using OpenShock.RepositoryServer.Services;
using OpenShock.RepositoryServer.Utils;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;
using Serilog;
using ValidationProblem = OpenShock.RepositoryServer.Problems.ValidationProblem;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile("appsettings.Container.json", optional: true, reloadOnChange: false)
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

// Since we use slim builders, this allows for HTTPS
builder.WebHost.UseKestrelHttpsConfiguration();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.RequestHeadersTimeout = TimeSpan.FromMilliseconds(3000);
});

builder.Host.UseSerilog((context, _, config) => config.ReadFrom.Configuration(context.Configuration));

var config = builder.GetAndRegisterOpenShockConfig<ApiConfig>();

// <---- ASP.NET ---->
builder.Services.AddExceptionHandler<OpenShockExceptionHandler>();

builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, AdminTokenAuthentication>(
        AuthSchemas.AdminToken, _ => { })
    .AddJwtBearer(AuthSchemas.CiCdToken, options =>
    {
        GitHubOidcAuthentication.Configure(options, config.Firmware.CiCd.Audience);
    });

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

// <---- Storage Service ---->
switch (config.Firmware.Storage.Type)
{
    case StorageType.BunnyCdn:
        var bunnyCdnConfig = config.Firmware.Storage.BunnyCdn
                             ?? throw new InvalidOperationException("BunnyCdn storage config is required when Type is BunnyCdn.");
        builder.Services.AddSingleton(bunnyCdnConfig);
        builder.Services.AddHttpClient<BunnyCdnStorageService>();
        builder.Services.AddSingleton<IStorageService>(sp => sp.GetRequiredService<BunnyCdnStorageService>());
        break;

    case StorageType.Local:
        var localConfig = config.Firmware.Storage.Local
                          ?? throw new InvalidOperationException("Local storage config is required when Type is Local.");
        builder.Services.AddSingleton(localConfig);
        builder.Services.AddSingleton<IStorageService, LocalStorageService>();
        break;

    case StorageType.S3:
        var s3Config = config.Firmware.Storage.S3
                       ?? throw new InvalidOperationException("S3 storage config is required when Type is S3.");
        builder.Services.AddSingleton(s3Config);
        builder.Services.AddSingleton<IStorageService, S3StorageService>();
        break;

    default:
        throw new InvalidOperationException($"Unknown storage type: {config.Firmware.Storage.Type}");
}

// <---- Discord notifications ---->
builder.Services.AddHttpClient<IDiscordNotificationService, DiscordNotificationService>();

// <---- Background cleanup ---->
builder.Services.AddHostedService<StagedReleaseCleanupService>();

// <---- Postgres EF Core ---->

builder.Services.AddDbContextPool<RepoServerContext>(dbBuilder =>
{
    RepoServerContext.ConfigureOptionsBuilder(dbBuilder, config.Db.Conn, config.Db.Debug);
});

builder.Services.AddPooledDbContextFactory<RepoServerContext>(dbBuilder =>
{
    RepoServerContext.ConfigureOptionsBuilder(dbBuilder, config.Db.Conn, config.Db.Debug);
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

var metricsAllowedIpNetworks = config.Metrics.AllowedNetworks.Select(IPNetwork.Parse);

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
