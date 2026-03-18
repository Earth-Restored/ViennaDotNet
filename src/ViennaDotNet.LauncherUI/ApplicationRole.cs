using Microsoft.AspNetCore.Identity;

namespace ViennaDotNet.LauncherUI;

public class ApplicationRole : IdentityRole
{
    public string Color { get; set; } = "#99AAB5";
    public int Position { get; set; }
}