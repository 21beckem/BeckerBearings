using System.Numerics;
using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>Coordinate transforms shared by floor fitting, mapping, and tracking.</summary>
public static class MapGeometry
{
    public static IReadOnlyList<SampledPoint> SamplePoints(StereoDepthFrame frame, int stride, double minRangeMeters, double maxRangeMeters, int rowStart = 0, byte minimumConfidence = 2)
    {
        if (stride < 1) throw new ArgumentOutOfRangeException(nameof(stride));
        var result = new List<SampledPoint>();
        for (var row = rowStart; row < frame.Height; row += stride)
        for (var column = 0; column < frame.Width; column += stride)
        {
            var index = row * frame.Width + column;
            if (frame.Confidence[index] < minimumConfidence || frame.Disparity[index] <= 0) continue;
            if (!TryReproject(frame.Q, column, row, frame.Disparity[index], out var point)) continue;
            if (point.Z >= minRangeMeters && point.Z <= maxRangeMeters) result.Add(new SampledPoint(point, row, column, frame.Confidence[index]));
        }
        return result;
    }

    public static bool TryReproject(IReadOnlyList<double> q, int column, int row, float disparity, out Vector3 point)
    {
        if (q.Count != 16) throw new ArgumentException("Q must have 16 coefficients.", nameof(q));
        var x = q[0] * column + q[1] * row + q[2] * disparity + q[3];
        var y = q[4] * column + q[5] * row + q[6] * disparity + q[7];
        var z = q[8] * column + q[9] * row + q[10] * disparity + q[11];
        var w = q[12] * column + q[13] * row + q[14] * disparity + q[15];
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) || !double.IsFinite(w) || w <= 1e-9) { point = default; return false; }
        point = new Vector3((float)(x / w), (float)(y / w), (float)(z / w));
        return float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);
    }

    public static Matrix4x4 GravityRotation(Vector3 normalCamera)
    {
        var up = Vector3.Normalize(normalCamera);
        var right = Vector3.Normalize(Vector3.UnitX - up * Vector3.Dot(Vector3.UnitX, up));
        var forward = -Vector3.Cross(right, up);
        return new Matrix4x4(right.X, right.Y, right.Z, 0, up.X, up.Y, up.Z, 0, forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1);
    }

    public static Vector3 CameraToGravity(Vector3 cameraPoint, Matrix4x4 gravityFromCamera, double cameraHeightMeters) =>
        Vector3.TransformNormal(cameraPoint, gravityFromCamera) + new Vector3(0, (float)cameraHeightMeters, 0);

    public static Vector2 GravityToWorldXZ(Vector3 gravityPoint, Pose2D pose)
    {
        var cosine = Math.Cos(pose.YawRadians); var sine = Math.Sin(pose.YawRadians);
        return new Vector2((float)(pose.XMeters + cosine * gravityPoint.X + sine * gravityPoint.Z),
            (float)(pose.ZMeters - sine * gravityPoint.X + cosine * gravityPoint.Z));
    }
}

public readonly record struct Pose2D(double XMeters = 0, double ZMeters = 0, double YawRadians = 0, bool TrackingOk = true);
public readonly record struct SampledPoint(Vector3 CameraPoint, int Row, int Column, byte Confidence);
