using System.IO.Compression;
using System.Text;
using Serilog;
using Solace.Common;
using Solace.Common.Utils;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Solace.Buildplate.Model;
#pragma warning restore IDE0130 // Namespace does not match folder structure

public sealed record WorldData(
    byte[] ServerData,
    int Size,
    int Offset,
    bool Night
)
{
    public static async Task<WorldData?> LoadFromZipAsync(Stream stream, ILogger logger, CancellationToken cancellationToken = default)
    {
        Dictionary<string, byte[]> worldFileContents = [];

        try
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.IsDirectory)
                    {
                        continue;
                    }

                    var entryPath = entry.FullName.AsSpan().Trim(['/', '\\']);

                    if (entryPath is not "buildplate_metadata.json")
                    {
                        // must be allocated here because of await
#pragma warning disable CA2014 // Do not use stackalloc in loops
                        Span<Range> parts = stackalloc Range[3];
#pragma warning restore CA2014 // Do not use stackalloc in loops
                        int partCount = entryPath.SplitAny(parts, ['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

                        if (partCount != 2)
                        {
                            continue;
                        }

                        if (entryPath[parts[0]] is not ("region" or "entities"))
                        {
                            continue;
                        }

                        if (entryPath[parts[1]] is not ("r.0.0.mca" or "r.0.-1.mca" or "r.-1.0.mca" or "r.-1.-1.mca"))
                        {
                            continue;
                        }
                    }

                    using (var entryStream = entry.Open())
                    using (var ms = new MemoryStream())
                    {
                        await entryStream.CopyToAsync(ms, cancellationToken);

                        worldFileContents[entry.FullName] = ms.ToArray();
                    }
                }
            }
        }
        catch (IOException ex)
        {
            logger.Error($"Could not read world file: {ex}");
            return null;
        }

        byte[] serverData;
        try
        {
            using (var zipStream = new MemoryStream())
            {
                using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    foreach (string dirName in (IEnumerable<string>)["region", "entities"])
                    {
                        foreach (string fileName in (IEnumerable<string>)["r.0.0.mca", "r.0.-1.mca", "r.-1.0.mca", "r.-1.-1.mca"])
                        {
                            string filePath = $"{dirName}/{fileName}";

                            if (!worldFileContents.TryGetValue(filePath, out byte[]? data))
                            {
                                Log.Error($"World file is missing {filePath}");
                                return null;
                            }

                            var entry = zip.CreateEntry(filePath, CompressionLevel.SmallestSize);
                            using (var entryStream = entry.Open())
                            {
                                entryStream.Write(data);
                            }
                        }
                    }
                }

                serverData = zipStream.ToArray();
            }
        }
        catch (IOException ex)
        {
            logger.Error($"Could not prepare server data: {ex}");
            return null;
        }

        byte[]? buildplateMetadataFileData = worldFileContents.GetValueOrDefault("buildplate_metadata.json");
        string? buildplateMetadataString = buildplateMetadataFileData is not null
            ? Encoding.UTF8.GetString(buildplateMetadataFileData)
            : null;

        return WorldData.Load(serverData, buildplateMetadataString, logger);

    }

    public static WorldData? Load(byte[] serverData, string? buildplateMetadataString, ILogger logger)
    {
        int size;
        int offset;
        bool night;

        try
        {
            if (buildplateMetadataString is null)
            {
                logger.Warning("World file does not contain buildplate_metadata.json, using default values");
                size = 16;
                offset = 63;
                night = false;
            }
            else
            {
                var buildplateMetadataVersion = Json.Deserialize<BuildplateMetadataVersion>(buildplateMetadataString);

                if (buildplateMetadataVersion is null)
                {
                    logger.Error("Invalid buildplate metadata");
                    return null;
                }

                switch (buildplateMetadataVersion.Version)
                {
                    case 1:
                        {
                            var buildplateMetadata = Json.Deserialize<BuildplateMetadataV1>(buildplateMetadataString);

                            if (buildplateMetadata is null)
                            {
                                logger.Error("Invalid buildplate metadata");
                                return null;
                            }

                            size = buildplateMetadata.Size;
                            offset = buildplateMetadata.Offset;
                            night = buildplateMetadata.Night;
                        }

                        break;
                    default:
                        {
                            logger.Error($"Unsupported buildplate metadata version {buildplateMetadataVersion.Version}");
                            return null;
                        }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Could not read buildplate metadata file: {ex}");
            return null;
        }

        if (size != 8 && size != 16 && size != 32)
        {
            logger.Error($"Invalid buildplate size {size}, must be on of: 8, 16, 32");
            return null;
        }

        return new WorldData(serverData, size, offset, night);
    }
}