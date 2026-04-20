using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ViennaDotNet.LauncherUI.Models.Db;

public class DbBuildplatePreview
{
    public int Id { get; set; }

    public string? PlayerId { get; set; }

    public required string BuildplateId { get; set; }

    public required byte[] PreviewData { get; set; }
}