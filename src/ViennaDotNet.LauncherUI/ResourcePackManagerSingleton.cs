using System.Diagnostics.CodeAnalysis;
using Serilog;
using ViennaDotNet.BuildplateRenderer;

namespace ViennaDotNet.LauncherUI;

internal static class ResourcePackManagerSingleton
{
    private static ResourcePackManager? resourcePackManager;
    private static readonly SemaphoreSlim resourcePackLock = new(1, 1);
    
    public static async Task<ResourcePackManager> GetResourcePackManagerAsync()
    {
        await EnsureResourcePackLoadedAsync();

        return resourcePackManager;
    }

    [MemberNotNull(nameof(resourcePackManager))]
    private static async Task EnsureResourcePackLoadedAsync()
    {
        if (resourcePackManager is not null)
        {
            return;
        }
    
        await resourcePackLock.WaitAsync();

        try
        {
            if (resourcePackManager is null)
            {
                var dir = new DirectoryInfo(Path.Combine(Settings.Instance.StaticDataPath ?? "", "resourcepacks", "java"));
                if (dir.Exists)
                {
                    resourcePackManager = await ResourcePackManager.LoadAllAsync(dir);
                    if (resourcePackManager.LoadedPackCount < 2)
                    {
                        Log.Warning($"Only loaded {resourcePackManager.LoadedPackCount} resourcepacks, make sure staticdata/resourcepacks/java contains minecraft/ and fountain/");
                        resourcePackManager = null;
                    }
                }
                else
                {
                    Log.Warning("Resource pack directory not found. Previews will likely fail.");
                }
            }
        }
        finally
        {
            resourcePackLock.Release();
        }
    }
}