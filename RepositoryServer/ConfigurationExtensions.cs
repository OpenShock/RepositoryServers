using System.Text;
using System.Text.Json;
namespace OpenShock.RepositoryServer;

public static class ConfigurationExtensions
{
    public static T GetAndRegisterOpenShockConfig<T>(this WebApplicationBuilder builder) where T : class
    {
#if DEBUG
        // GetDebugView() prints every configuration value AND the process environment, which includes
        // the admin token, the database password, storage credentials and Discord webhook URLs. Only
        // the resolved keys are printed, which is what is actually useful for diagnosing binding.
        Console.WriteLine("Configuration keys:");
        foreach (var (key, value) in builder.Configuration.AsEnumerable().OrderBy(kv => kv.Key))
        {
            if (value is null) continue;
            Console.WriteLine($"  {key} = {RedactIfSensitive(key, value)}");
        }
#endif

        var config = builder.Configuration
            .Get<T>() ?? throw new Exception("Couldn't bind config, check config file");

        var openshockSection = builder.Configuration.GetChildren()
            .FirstOrDefault(x => x.Key.Equals("openshock", StringComparison.InvariantCultureIgnoreCase));

        if (openshockSection != null)
        {
            openshockSection.Bind(config);
        }

        MiniValidation.MiniValidator.TryValidate(config, true, true, out var errors);
        if (errors.Count > 0)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Error validating config, please fix your configuration / environment variables");
            sb.AppendLine("Found the following errors:");
            foreach (var error in errors)
            {
                sb.AppendLine($"Error on field [{error.Key}] reason: {string.Join(", ", error.Value)}");
            }

            Console.WriteLine(sb.ToString());
            Environment.Exit(-10);
        }


        builder.Services.AddSingleton<T>(config);

        return config;
    }

#if DEBUG
    private static readonly string[] SensitiveKeyFragments =
        ["token", "password", "secret", "key", "conn", "webhook"];

    /// <summary>
    /// Masks values whose key looks credential-bearing. Name-based rather than attribute-based so a
    /// newly added secret is redacted by default instead of needing to be remembered.
    /// </summary>
    private static string RedactIfSensitive(string key, string value)
    {
        foreach (var fragment in SensitiveKeyFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return $"*** ({value.Length} chars)";
            }
        }
        return value;
    }
#endif
}
