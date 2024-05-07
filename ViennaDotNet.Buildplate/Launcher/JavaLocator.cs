using Serilog;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.Buildplate.Launcher
{
    public static class JavaLocator
    {
        public static string locateJava()
        {
            Log.Information("Trying to locate Java");

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
                Log.Information("Trying JAVA_HOME");
                try
                {
                    FileInfo file = new FileInfo(Path.Combine(javaHome, "bin", "java"));
                    if (file.CanExecute())
                    {
                        string path = file.FullName;
                        Log.Information($"Using Java from JAVA_HOME ({path})");
                        return path;
                    }
                    file = new FileInfo(Path.Combine(javaHome, "bin", "java.exe"));
                    if (file.CanExecute())
                    {
                        string path = file.FullName;
                        Log.Information($"Using Java from JAVA_HOME ({path})");
                        return path;
                    }
                }
                catch (IOException)
                {
                    // empty
                }
                Log.Information("Java from JAVA_HOME is not suitable (does not exist or cannot be accessed)");
            }
            else
                Log.Information("JAVA_HOME is not set");

            Log.Information("Using \"java\"");
            return "java";
        }
    }
}
