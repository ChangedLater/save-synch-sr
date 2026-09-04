using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarRuptureSync.Models;

namespace StarRuptureSync.Services;

/// <summary>Loads and saves <see cref="AppSettings"/>, protecting the git token with DPAPI.</summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt settings file – fall back to defaults rather than crash on launch.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureRoot();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsFile, json);
    }

    /// <summary>Encrypt and store the git personal access token on <paramref name="settings"/>.</summary>
    public void SetApiKey(AppSettings settings, string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            settings.ApiKeyProtected = "";
            return;
        }

        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        settings.ApiKeyProtected = Convert.ToBase64String(protectedBytes);
    }

    /// <summary>Decrypt the stored git token, or return an empty string if none/undecryptable.</summary>
    public string GetApiKey(AppSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ApiKeyProtected))
            return "";

        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(settings.ApiKeyProtected),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";
        }
    }
}
