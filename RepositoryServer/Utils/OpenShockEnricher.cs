using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Adds the request's caller details to every log event.
/// </summary>
/// <remarks>
/// Nothing references this in code. It is wired up by name from the Serilog section of
/// appsettings.json - <c>"Enrich": [ ... "WithOpenShockEnricher" ]</c> - which Serilog resolves by
/// reflection against <see cref="OpenShockEnricherLoggerConfigurationExtensions"/>. A search for
/// callers therefore finds none, and removing either type as dead code drops these properties from
/// every log line with nothing failing to build. <see cref="ConnectionDetailsFetcher"/> is used only
/// from here and is reachable for the same reason.
/// </remarks>
public sealed class OpenShockEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _contextAccessor;

    public OpenShockEnricher() : this(new HttpContextAccessor())
    {
    }

    public OpenShockEnricher(IHttpContextAccessor contextAccessor)
    {
        _contextAccessor = contextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if(_contextAccessor.HttpContext == null) return;

        var ctx = _contextAccessor.HttpContext;
    
        logEvent.AddOrUpdateProperty(new LogEventProperty("UserIp", new ScalarValue(ctx.GetRemoteIP())));
        logEvent.AddOrUpdateProperty(new LogEventProperty("UserAgent", new ScalarValue(ctx.GetUserAgent())));
        logEvent.AddOrUpdateProperty(new LogEventProperty("RequestHost", new ScalarValue(ctx.Request.Headers[HeaderNames.Host].FirstOrDefault())));
        logEvent.AddOrUpdateProperty(new LogEventProperty("RequestReferer", new ScalarValue(ctx.Request.Headers[HeaderNames.Referer].FirstOrDefault())));
        logEvent.AddOrUpdateProperty(new LogEventProperty("CF-IPCountry", new ScalarValue(ctx.GetCFIPCountry())));
    }
}

public static class OpenShockEnricherLoggerConfigurationExtensions
{
    public static LoggerConfiguration WithOpenShockEnricher(this LoggerEnrichmentConfiguration enrichmentConfiguration)
    {
        if (enrichmentConfiguration == null) throw new ArgumentNullException(nameof(enrichmentConfiguration));
        return enrichmentConfiguration.With<OpenShockEnricher>();
    }
}