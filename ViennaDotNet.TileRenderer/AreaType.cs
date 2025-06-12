using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.TileRenderer;

// Taken from the resource pack (iirc)
internal enum AreaType
{
    RESTRICTED_AREA = 0x00,       // restricted areas (bad for gameplay/PR)
    HIGHWAY_MAJOR = 0x11,         // looks like road
    HIGHWAY_MINOR = 0x22,         // road
    HIGHWAY_SERVICE = 0x33,       // minor road
    CYCLE_PATH = 0x44,            // (PEDESTRIAN_WALKWAYS) seems to be cycle path
    MOUNTAIN = 0x55,              // rock?
    SAND = 0x66,                  // sand
    PIER = 0x77,                  // docks
    FOOTPATH = 0x88,              // actual footpaths
    WATER = 0x99,                 // water
    ATHLETIC_FIELD = 0xAA,        // playgrounds
    OPEN_PRIVATE_AREA = 0xBB,     // farmland
    OPEN_PUBLIC_AREA = 0xCC,      // grass/parks
    FOREST = 0xDD,                // trees
    BUILDING = 0xEE,              // buildings
    BASE_BACKGROUND = 0xFF        // unclassified, looks like "default grass"
}
