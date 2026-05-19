using System.ComponentModel.DataAnnotations;
using Solace.Common.Utils;
using Solace.DB.Models.Global;
using Solace.DB.Models.Player;
using Solace.DB.Models.Player.Workshop;

namespace Solace.DB.Models;

public sealed class Account : IEntityWithId<Guid>, IMergeable<Account>
{
    public const string DefaultPictureUrl = "images/default_pfp.png";

    public required Guid Id { get; set; }

    public required long CreatedDate { get; set; }

    public required string Username { get; set; }

    public required string? ProfilePictureUrl { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    [MaxLength(16)]
    public required byte[] PasswordSalt { get; set; }

    [MaxLength(64)]
    public required byte[] PasswordHash { get; set; }

    public ProfileEF? Profile { get; set; }

    public ActivityLogEF? ActivityLog { get; set; }

    public BoostsEF? Boosts { get; set; }

    public ICollection<BuildplateEF> Buildplates { get; set; } = [];

    public HotbarEF? Hotbar { get; set; }

    public InventoryEF? Inventory { get; set; }

    public JournalEF? Journal { get; set; }

    public RedeemedTappablesEF? RedeemedTappables { get; set; }

    public TokensEF? Tokens { get; set; }

    public CraftingSlotsEF? CraftingSlots { get; set; }

    public SmeltingSlotsEF? SmeltingSlots { get; set; }

    public ICollection<SharedBuildplateEF> SharedBuildplates { get; set; } = [];

    public async Task MergeWith(Account other, ValueMerger merger)
    {
        merger.CurrentUserId = Id.ToString();
        merger.CurrentUsername = Username;

        CreatedDate = await merger.AutoMergeMin(CreatedDate, other.CreatedDate, "Created date");
        Username = await merger.AutoMerge(Username, other.Username, nameof(Username), null);
        FirstName = await merger.AutoMerge(FirstName!, other.FirstName!, "First name", null);
        LastName = await merger.AutoMerge(LastName!, other.LastName!, "Last name", null);

        if (!PasswordSalt.SequenceEqual(other.PasswordSalt) || !PasswordHash.SequenceEqual(other.PasswordHash))
        {
            if (await merger.AutoMerge("?current?", "?import?", "Password", null) == "?import?")
            {
                PasswordSalt = other.PasswordSalt;
                PasswordHash = other.PasswordHash;
            }
        }
    }

    public sealed class Legacy
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
}