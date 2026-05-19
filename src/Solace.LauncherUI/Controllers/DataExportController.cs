using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Solace.LauncherUI;

[ApiController]
[Authorize(Policy = Permissions.ExportData)]
[Route("api/data/export")]
internal sealed class DataExportController : ControllerBase
{
    private readonly FileInfo _earthDB;
    private readonly DirectoryInfo _objectStore;

    public DataExportController()
    {
        _earthDB = new FileInfo(Settings.Instance.EarthDatabaseConnectionString!);
        _objectStore = new DirectoryInfo(Path.Combine(Program.DataDirRelative, Program.ObjectStoreDirName));
    }

    [HttpGet]
    public async Task<FileStreamHttpResult> Export()
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (_earthDB.Exists)
            {
                await archive.CreateEntryFromFileAsync(_earthDB.FullName, "earth.db");
            }

            if (_objectStore.Exists)
            {
                foreach (var objFile in _objectStore.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    var objFileName = Path.GetRelativePath(_objectStore.FullName, objFile.FullName);
                    await archive.CreateEntryFromFileAsync(objFile.FullName, $"object_store/{objFileName}");
                }
            }
        }

        stream.Position = 0;

        string fileName = $"ServerData_{DateTimeOffset.UtcNow:yyyy-MM-dd_HH:mm:ss}.zip";

        return TypedResults.File(stream, "application/zip", fileName);
    }
}