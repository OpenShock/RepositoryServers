using System.Security.Claims;
using Microsoft.Net.Http.Headers;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace OpenShock.Desktop.RepositoryServer.Utils;

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