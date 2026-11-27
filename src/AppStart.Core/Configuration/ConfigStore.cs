using AppStart.Core.Security;

namespace AppStart.Core.Configuration;

/// <summary>
/// Loads apps.json for the service: refuses when the file or its folder can be changed by
/// non-admins, or when the content is invalid (spec app-launch). Read fresh on every request.
/// </summary>
public static class ConfigStore
{
    public static ConfigLoadResult Load(string? path = null)
    {
        path ??= AppStartConstants.ConfigPath;

        if (!File.Exists(path))
        {
            return ConfigLoadResult.Fail($"{path} is missing.");
        }

        var folder = Path.GetDirectoryName(path)!;
        foreach (var target in new[] { folder, path })
        {
            var violations = WriteAccessPolicy.FindViolations(target);
            if (violations.Count > 0)
            {
                return ConfigLoadResult.Fail($"{target} is not protected: {string.Join(" ", violations)}");
            }
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            return ConfigLoadResult.Fail($"{path} cannot be read: {ex.Message}");
        }

        return AppsConfigSerializer.Parse(json);
    }
}
