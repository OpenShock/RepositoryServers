using OpenShock.RepositoryServer.Problems;

namespace OpenShock.RepositoryServer.Errors;

public static class ExceptionError
{
    public static ExceptionProblem Exception => new ExceptionProblem();
}