using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Applies a <c>Cache-Control</c> header per firmware-api-spec.md §8: public max-age on
/// 2xx responses, <c>no-store</c> on errors. Apply per-action on public read endpoints.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CacheControlAttribute : ActionFilterAttribute
{
    public int MaxAgeSeconds { get; }
    public bool Immutable { get; }

    public CacheControlAttribute(int maxAgeSeconds, bool immutable = false)
    {
        MaxAgeSeconds = maxAgeSeconds;
        Immutable = immutable;
    }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is ObjectResult { StatusCode: >= 200 and < 300 } or StatusCodeResult { StatusCode: >= 200 and < 300 })
        {
            var value = Immutable
                ? $"public, max-age={MaxAgeSeconds}, immutable"
                : $"public, max-age={MaxAgeSeconds}";
            context.HttpContext.Response.Headers["Cache-Control"] = value;
        }
        else
        {
            context.HttpContext.Response.Headers["Cache-Control"] = "no-store";
        }

        base.OnResultExecuting(context);
    }
}
