namespace Solace.DB.Models.Player;

public sealed class Profile : IEquatable<Profile>
{
    public int Health { get; set; }
    public int Experience { get; set; }
    public int Level { get; set; }
    public Rubies Rubies { get; set; }

    public Profile()
    {
        Health = 20;
        Experience = 0;
        Level = 1;
        Rubies = new Rubies();
    }

    public bool Equals(Profile? other)
        => other is not null && Health == other.Health && Experience == other.Experience && Level == other.Level && Rubies.Equals(other.Rubies);

    public override bool Equals(object? obj)
        => Equals(obj as Profile);

    public override int GetHashCode()
        => HashCode.Combine(Health, Experience, Level, Rubies);
}
