using System.Diagnostics;
using System.Text.Json.Serialization;

namespace ViennaDotNet.DB.Models.Player;

public sealed class Rubies
{
    public Rubies()
    {
        Purchased = 0;
        Earned = 0;
    }

    public int Purchased { get; set; }

    public int Earned { get; set; }

    [JsonIgnore]
    public int Total => Purchased + Earned;

    /// <summary>
    /// Tries to spend <paramref name="amount"/> rubies
    /// </summary>
    /// <param name="amount">The amount of rubies to spend</param>
    /// <returns>If there were enought rubies to spend <paramref name="amount"/></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public bool Spend(int amount)
    {
        if (amount > Total)
        {
            return false;
        }

        // TODO: in what order should purchased/earned rubies be spent?
        if (amount > Purchased)
        {
            amount -= Purchased;
            Purchased = 0;
        }
        else
        {
            Purchased -= amount;
            amount = 0;
        }

        if (amount > 0)
        {
            Earned -= amount;
        }

        if (Purchased < 0 || Earned < 0)
        {
            throw new UnreachableException();
        }

        return true;
    }
}
