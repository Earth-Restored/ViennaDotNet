using System.ComponentModel.DataAnnotations;

namespace ViennaDotNet.ApiServer.Models;

public class Account
{
    public required string Id { get; set; }

    public required long CreatedDate { get; set; }

    public required string Username { get; set; }

    public required string ProfilePictureUrl { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    [MaxLength(16)]
    public required byte[] PasswordSalt { get; set; }

    [MaxLength(64)]
    public required byte[] PasswordHash { get; set; }
}
