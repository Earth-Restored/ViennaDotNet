using Serilog;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.Common;

public static class JavaLocator
{
    public static string Locate(ILogger? logger = null)
    {
        logger ??= Log.Logger;

        logger.Information("Trying to locate Java");

        string? javaHome;
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            javaHome = Environment.GetEnvironmentVariable("JAVA_HOME", EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(javaHome))
                javaHome = Environment.GetEnvironmentVariable("JAVA_HOME", EnvironmentVariableTarget.Machine);
        }
        else
            javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            logger.Information("Trying JAVA_HOME");
            try
            {
                var file = new FileInfo(Path.Combine(javaHome, "bin", "java"));
                if (file.CanExecute())
                {
                    string path = file.FullName;
                    logger.Information($"Using Java from JAVA_HOME ({path})");
                    return path;
                }

                file = new FileInfo(Path.Combine(javaHome, "bin", "java.exe"));
                if (file.CanExecute())
                {
                    string path = file.FullName;
                    logger.Information($"Using Java from JAVA_HOME ({path})");
                    return path;
                }
            }
            catch (IOException)
            {
                // empty
            }

            logger.Information("Java from JAVA_HOME is not suitable (does not exist or cannot be accessed)");
        }
        else
        {
            logger.Information("JAVA_HOME is not set");
        }

        logger.Information("Using \"java\"");
        return "java";
    }
}
