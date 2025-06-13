using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.TileRenderer;

internal sealed record RenderTileRequest(double Lat, double Lon, int Zoom);