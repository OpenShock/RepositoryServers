using System.Text.Json;

namespace OpenShock.RepositoryServer.JsonSerialization;

/// <summary>
/// The single definition of how this server serializes JSON.
/// </summary>
/// <remarks>
/// <see cref="Default"/> exists for the code paths that write a response outside MVC - authentication
/// handlers, the authorization result handler, the exception handler - so a problem body written from
/// middleware is shaped exactly like one written from a controller.
/// </remarks>
public static class JsonOptions
{
    static JsonOptions()
    {
        ConfigureDefault(Default);
    }

    public static void ConfigureDefault(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.Converters.Add(new SemVersionConverter());
    }

    public static readonly JsonSerializerOptions Default = new();
}
