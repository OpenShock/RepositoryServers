namespace OpenShock.Desktop.RepositoryServer.Utils;

public static class UriExtensions
{
    public static Uri? ToUri(this string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var result))
        {
            return null;
        }
        
        return result;
    }
    
}