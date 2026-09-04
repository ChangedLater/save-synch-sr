using System.IO;
using System.Security.Cryptography;

namespace StarRuptureSync.Services;

public static class HashUtil
{
    /// <summary>Lower-case hex SHA-256 of a file's contents, or null if the file is missing.</summary>
    public static string? HashFile(string path)
    {
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}
