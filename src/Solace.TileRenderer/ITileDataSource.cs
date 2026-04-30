using ViennaDotNet.TileRenderer.Wkb;

namespace ViennaDotNet.TileRenderer;

public interface ITileDataSource : IDisposable
{
    string GetTagMapJson(StaticData.TileRenderer tileRenderer);

    Task<List<List<IWKBObject>>> GetTileAsync(RenderContext ctx, int zoom, int tileX, int tileY, CancellationToken cancellationToken = default);
}
