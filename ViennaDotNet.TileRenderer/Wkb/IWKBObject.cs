using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.TileRenderer.Wkb;

internal interface IWKBObject
{
    bool ByteOrder { get; set; }

    uint WkbType { get; set; }

    static abstract void Load(BinaryReader reader);

    void Render(Tile tile, double r, double g, double b, double strokeWidth);
}
