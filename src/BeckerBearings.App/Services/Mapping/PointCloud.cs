using System.Numerics;
using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Mapping;

public sealed record PointCloud(Vector3[] Points, byte[] Intensity)
{
    public static PointCloud FromDepth(StereoDepthFrame depth, byte minimumConfidence = 2)
    {
        if (minimumConfidence is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        var points = new List<Vector3>(); var intensity = new List<byte>();
        for (var row = 0; row < depth.Height; row++)
        for (var col = 0; col < depth.Width; col++)
        {
            var index = row * depth.Width + col;
            if (depth.Confidence[index] < minimumConfidence || depth.Disparity[index] <= 0 || !MapGeometry.TryReproject(depth.Q, col, row, depth.Disparity[index], out var point) || point.Z <= 0) continue;
            points.Add(point); intensity.Add(depth.Gray[index]);
        }
        return new PointCloud(points.ToArray(), intensity.ToArray());
    }

    public PointCloud Downsample(double voxelMeters)
    {
        if (!double.IsFinite(voxelMeters) || voxelMeters <= 0) throw new ArgumentOutOfRangeException(nameof(voxelMeters));
        var voxels = new Dictionary<(int X, int Y, int Z), (Vector3 Sum, int Intensity, int Count)>();
        for (var i = 0; i < Points.Length; i++)
        {
            var p = Points[i]; var key = ((int)MathF.Floor(p.X / (float)voxelMeters), (int)MathF.Floor(p.Y / (float)voxelMeters), (int)MathF.Floor(p.Z / (float)voxelMeters));
            voxels.TryGetValue(key, out var value); voxels[key] = (value.Sum + p, value.Intensity + Intensity[i], value.Count + 1);
        }
        return new PointCloud(voxels.Values.Select(v => v.Sum / v.Count).ToArray(), voxels.Values.Select(v => (byte)(v.Intensity / v.Count)).ToArray());
    }

    public async Task ExportPlyAsync(string path, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var output = File.Create(path);
        var header = System.Text.Encoding.ASCII.GetBytes($"ply\nformat binary_little_endian 1.0\nelement vertex {Points.Length}\nproperty float x\nproperty float y\nproperty float z\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nend_header\n");
        await output.WriteAsync(header, cancellationToken);
        var buffer = new byte[15];
        for (var i = 0; i < Points.Length; i++)
        {
            BitConverter.GetBytes(Points[i].X).CopyTo(buffer, 0); BitConverter.GetBytes(Points[i].Y).CopyTo(buffer, 4); BitConverter.GetBytes(Points[i].Z).CopyTo(buffer, 8);
            buffer[12] = buffer[13] = buffer[14] = Intensity[i]; await output.WriteAsync(buffer, cancellationToken);
        }
    }
}
