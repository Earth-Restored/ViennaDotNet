namespace Solace.Common.Utils;

public static class RandomExtensions
{
    public static float NextSingle(this Random random, float min, float max)
    {
        if (min >= max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "Minimum value must be less than maximum value.");
        }

        float range = max - min;
        float sample = random.NextSingle() * range;
        return sample + min;
    }
}
