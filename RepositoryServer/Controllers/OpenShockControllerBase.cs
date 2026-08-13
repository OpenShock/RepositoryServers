using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;

namespace OpenShock.RepositoryServer.Controllers;

[Consumes(MediaTypeNames.Application.Json)]
public class OpenShockControllerBase : OpenShock.Internal.Common.OpenShockControllerBase
{
}
