using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using OpenShock.Desktop.RepositoryServer.Errors;

namespace OpenShock.Desktop.RepositoryServer.ExceptionHandler;

public sealed class OpenShockExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<OpenShockExceptionHandler> _logger;

    public OpenShockExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<OpenShockExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }
    
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An unhandled exception occurred while processing the request.");
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        
        var responseObject = ExceptionError.Exception;
        responseObject.AddContext(context);

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = responseObject
        });
    }
}