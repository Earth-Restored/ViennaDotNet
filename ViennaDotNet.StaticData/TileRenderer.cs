using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.StaticData;

public sealed class TileRenderer
{
    public TileRenderer(string dir)
    {
        try
        {
            TagMapJson = File.ReadAllText(Path.Combine(dir, "tagMap.json"));
        }
        catch (Exception exception)
        {
            throw new StaticDataException(null, exception);
        }
    }

    public string TagMapJson { get; }
}
