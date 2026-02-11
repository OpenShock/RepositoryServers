using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using OpenShock.RepositoryServer.Problems;

namespace OpenShock.RepositoryServer.Controllers;

[Consumes(MediaTypeNames.Application.Json)]
public class OpenShockControllerBase : ControllerBase
{
    [NonAction]
    public ObjectResult Problem(OpenShockProblem problem) => problem.ToObjectResult(HttpContext);
}