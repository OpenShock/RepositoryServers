namespace OpenShock.RepositoryServer.Enums;

/// <summary>
/// Source-code provider for the shared <c>repositories</c> table. Only GitHub is
/// supported today; new providers require a matching OIDC handler and URL builder.
/// Member name uses single-word casing so the Postgres enum value is <c>github</c>.
/// </summary>
public enum RepositoryProvider
{
    Github
}
