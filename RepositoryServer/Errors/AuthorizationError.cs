using OpenShock.RepositoryServer.Problems.CustomProblems;

namespace OpenShock.RepositoryServer.Errors;

public static class AuthorizationError
{
    public static PolicyNotMetProblem PolicyNotMet(IEnumerable<string> failedRequirements) => new(failedRequirements);
}
