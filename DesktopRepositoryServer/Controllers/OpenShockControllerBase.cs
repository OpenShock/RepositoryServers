using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using OpenShock.Desktop.RepositoryServer.Problems;

namespace OpenShock.Desktop.RepositoryServer.Controllers;

[Consumes(MediaTypeNames.Application.Json)]
public class OpenShockControllerBase : ControllerBase
{
    [NonAction]
    public ObjectResult Problem(OpenShockProblem problem) => problem.ToObjectResult(HttpContext);
}