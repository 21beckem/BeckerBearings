namespace BeckerBearings.App.Services.Mapping;

/// <summary>Data-only replacement for map_view.py. Consumers choose how to display its BGR raster.</summary>
public static class MapRasterizer
{
    public static MapRaster Render(ObstacleMap map, int pixelsPerCell = 4)
    {
        var state = map.StateGrid(); var side = (int)Math.Sqrt(state.Length); var width = side * pixelsPerCell; var bgr = new byte[width * width * 3];
        for (var cell = 0; cell < state.Length; cell++)
        {
            var color = state[cell] switch { ObstacleMap.Free => (B: (byte)75, G: (byte)185, R: (byte)80), ObstacleMap.Obstacle => (B: (byte)40, G: (byte)40, R: (byte)230), ObstacleMap.Danger => (B: (byte)0, G: (byte)175, R: (byte)255), ObstacleMap.Remembered => (B: (byte)150, G: (byte)90, R: (byte)210), _ => (B: (byte)48, G: (byte)48, R: (byte)48) };
            var cellX = cell % side; var cellY = cell / side;
            for (var y = 0; y < pixelsPerCell; y++) for (var x = 0; x < pixelsPerCell; x++) { var index = ((cellY * pixelsPerCell + y) * width + cellX * pixelsPerCell + x) * 3; bgr[index] = color.B; bgr[index + 1] = color.G; bgr[index + 2] = color.R; }
        }
        return new MapRaster(bgr, width, width);
    }
}

public sealed record MapRaster(byte[] Bgr, int Width, int Height);
