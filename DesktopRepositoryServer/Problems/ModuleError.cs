using System.Net;

namespace OpenShock.Desktop.RepositoryServer.Problems;

public static class ModuleError
{
    public static OpenShockProblem ModuleNotFound => new OpenShockProblem("Module.NotFound", "The referenced module was not found", HttpStatusCode.NotFound);
}