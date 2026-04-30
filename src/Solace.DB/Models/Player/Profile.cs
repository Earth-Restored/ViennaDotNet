namespace ViennaDotNet.DB.Models.Player;

public sealed class Profile
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
}
