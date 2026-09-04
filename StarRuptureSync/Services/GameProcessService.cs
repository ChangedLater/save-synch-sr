using System.Diagnostics;

namespace StarRuptureSync.Services;

/// <summary>Detects whether StarRupture is currently running so we never overwrite a live save.</summary>
public class GameProcessService
{
    private static readonly string[] ProcessNames =
    {
        "StarRupture",
        "StarRupture-Win64-Shipping",
        "StarRuptureClient-Win64-Shipping",
        "StarRuptureGameSteam-Win64-Shipping",
        "StarRuptureClient"
    };

    public bool IsRunning(out string? matchedProcess)
    {
        foreach (var name in ProcessNames)
        {
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            try
            {
                if (procs.Length > 0)
                {
                    matchedProcess = name;
                    return true;
                }
            }
            finally
            {
                foreach (var p in procs)
                    p.Dispose();
            }
        }

        matchedProcess = null;
        return false;
    }
}
