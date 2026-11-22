using System.Numerics;
using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>Constrained, temporally smoothed RANSAC floor estimator.</summary>
public sealed class GroundPlaneEstimator
{
    private readonly MappingOptions _options;
    private readonly Random _random = new(17);
    private Vector3? _normal;
    private double? _height;
    private double? _lastGoodTimestamp;

    public GroundPlaneEstimator(MappingOptions options) => _options = options;

    public FloorEstimate Fit(StereoDepthFrame depth, double timestampSeconds)
    {
        var rowStart = (int)(depth.Height * _options.FloorImageStartFraction);
        var candidates = MapGeometry.SamplePoints(depth, _options.SampleStride, _options.FloorMinRangeMeters, _options.FloorMaxRangeMeters, rowStart)
            .Select(sample => sample.CameraPoint).ToArray();
        var candidate = Estimate(candidates);
        var fresh = candidate is not null;
        var inliers = 0;
        if (candidate is { } plane)
        {
            var normal = plane.Normal; var height = plane.Height;
            if (_normal is { } previousNormal)
            {
                normal = Vector3.Normalize(Vector3.Lerp(previousNormal, normal, (float)_options.FloorSmoothing));
                height = _options.FloorSmoothing * height + (1 - _options.FloorSmoothing) * _height!.Value;
            }
            _normal = normal; _height = height; _lastGoodTimestamp = timestampSeconds; inliers = plane.Inliers;
        }
        var staleSeconds = _lastGoodTimestamp is null ? double.PositiveInfinity : Math.Max(0, timestampSeconds - _lastGoodTimestamp.Value);
        var usable = _normal is not null && staleSeconds <= _options.FloorStaleSeconds;
        return new FloorEstimate(usable ? _normal : null, usable ? _height : null,
            usable ? MapGeometry.GravityRotation(_normal!.Value) : null, fresh, usable, staleSeconds, inliers, candidates.Length);
    }

    private PlaneCandidate? Estimate(Vector3[] source)
    {
        if (source.Length < _options.FloorMinInliers) return null;
        var points = source.Length > _options.FloorRansacSamples ? source.OrderBy(_ => _random.Next()).Take(_options.FloorRansacSamples).ToArray() : source;
        var prior = _normal ?? -Vector3.UnitY;
        var guided = EstimateFromPrior(points);
        var tiltCosine = Math.Cos(DegreesToRadians(_options.FloorMaxTiltDegrees));
        var uprightCosine = Math.Cos(DegreesToRadians(_options.FloorMaxAbsoluteTiltDegrees));
        PlaneCandidate? best = null;
        for (var attempt = 0; attempt < _options.FloorIterations; attempt++)
        {
            var a = points[_random.Next(points.Length)]; var b = points[_random.Next(points.Length)]; var c = points[_random.Next(points.Length)];
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-8) continue;
            normal = Vector3.Normalize(normal); if (normal.Y > 0) normal = -normal;
            var height = -Vector3.Dot(normal, a);
            if (Vector3.Dot(normal, prior) < tiltCosine || -normal.Y < uprightCosine || height < _options.FloorMinHeightMeters || height > _options.FloorMaxHeightMeters) continue;
            var inliers = points.Where(point => Math.Abs(Vector3.Dot(point, normal) + height) <= _options.FloorInlierMeters).ToArray();
            if (inliers.Length < _options.FloorMinInliers || inliers.Length < _options.FloorMinFraction * points.Length || best is { } existing && inliers.Length <= existing.Inliers) continue;
            var refined = FitLeastSquares(inliers);
            if (refined is null || Vector3.Dot(refined.Value.Normal, prior) < tiltCosine || -refined.Value.Normal.Y < uprightCosine || refined.Value.Height < _options.FloorMinHeightMeters || refined.Value.Height > _options.FloorMaxHeightMeters) continue;
            var refinedCount = points.Count(point => Math.Abs(Vector3.Dot(point, refined.Value.Normal) + refined.Value.Height) <= _options.FloorInlierMeters);
            best = new PlaneCandidate(refined.Value.Normal, refined.Value.Height, refinedCount);
        }
        return best ?? guided;
    }

    private PlaneCandidate? EstimateFromPrior(Vector3[] points)
    {
        if (_normal is not { } previous || _height is not { } previousHeight) return null;
        var near = points.Where(point => Math.Abs(Vector3.Dot(point, previous) + (float)previousHeight) <= _options.FloorPriorBandMeters).ToArray();
        if (near.Length < _options.FloorPriorMinInliers ||
            near.Max(point => point.X) - near.Min(point => point.X) < _options.FloorPriorMinSpanXMeters ||
            near.Max(point => point.Z) - near.Min(point => point.Z) < _options.FloorPriorMinSpanZMeters) return null;
        var refined = FitLeastSquares(near);
        if (refined is null) return null;
        var normal = refined.Value.Normal; var height = refined.Value.Height;
        var inliers = points.Count(point => Math.Abs(Vector3.Dot(point, normal) + (float)height) <= _options.FloorInlierMeters);
        var support = points.Where(point => Math.Abs(Vector3.Dot(point, normal) + (float)height) <= _options.FloorInlierMeters).ToArray();
        if (inliers < _options.FloorPriorMinInliers || inliers < _options.FloorPriorMinFraction * points.Length ||
            support.Length == 0 || support.Max(point => point.X) - support.Min(point => point.X) < _options.FloorPriorMinSpanXMeters ||
            support.Max(point => point.Z) - support.Min(point => point.Z) < _options.FloorPriorMinSpanZMeters ||
            Vector3.Dot(normal, previous) < Math.Cos(DegreesToRadians(_options.FloorPriorMaxTiltDegrees)) ||
            -normal.Y < Math.Cos(DegreesToRadians(_options.FloorMaxAbsoluteTiltDegrees)) ||
            Math.Abs(height - previousHeight) > _options.FloorPriorMaxHeightChangeMeters ||
            height < _options.FloorMinHeightMeters || height > _options.FloorMaxHeightMeters) return null;
        return new PlaneCandidate(normal, height, inliers);
    }

    private static (Vector3 Normal, double Height)? FitLeastSquares(Vector3[] points)
    {
        var center = new Vector3(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var point in points) { var v = point - center; xx += v.X * v.X; xy += v.X * v.Y; xz += v.X * v.Z; yy += v.Y * v.Y; yz += v.Y * v.Z; zz += v.Z * v.Z; }
        var normal = SmallestEigenvector(new[,] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } });
        if (normal.LengthSquared() < 1e-8) return null;
        normal = Vector3.Normalize(normal); if (normal.Y > 0) normal = -normal;
        return (normal, -Vector3.Dot(normal, center));
    }

    // Jacobi diagonalization is ample for the fixed 3x3 covariance matrix and avoids a UI/platform dependency.
    private static Vector3 SmallestEigenvector(double[,] values)
    {
        var vectors = new[,] { { 1d, 0d, 0d }, { 0d, 1d, 0d }, { 0d, 0d, 1d } };
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var (p, q) = (0, 1); var greatest = Math.Abs(values[0, 1]);
            foreach (var pair in new[] { (0, 2), (1, 2) }) if (Math.Abs(values[pair.Item1, pair.Item2]) > greatest) { (p, q) = pair; greatest = Math.Abs(values[p, q]); }
            if (greatest < 1e-10) break;
            var angle = .5 * Math.Atan2(2 * values[p, q], values[q, q] - values[p, p]); var cosine = Math.Cos(angle); var sine = Math.Sin(angle);
            for (var index = 0; index < 3; index++) if (index != p && index != q)
            { var ip = values[index, p]; var iq = values[index, q]; values[index, p] = values[p, index] = cosine * ip - sine * iq; values[index, q] = values[q, index] = sine * ip + cosine * iq; }
            var pp = values[p, p]; var qq = values[q, q]; var pq = values[p, q];
            values[p, p] = cosine * cosine * pp - 2 * sine * cosine * pq + sine * sine * qq; values[q, q] = sine * sine * pp + 2 * sine * cosine * pq + cosine * cosine * qq; values[p, q] = values[q, p] = 0;
            for (var index = 0; index < 3; index++) { var vp = vectors[index, p]; var vq = vectors[index, q]; vectors[index, p] = cosine * vp - sine * vq; vectors[index, q] = sine * vp + cosine * vq; }
        }
        var least = Enumerable.Range(0, 3).OrderBy(index => values[index, index]).First();
        return new Vector3((float)vectors[0, least], (float)vectors[1, least], (float)vectors[2, least]);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
    private readonly record struct PlaneCandidate(Vector3 Normal, double Height, int Inliers);
}

public sealed record FloorEstimate(Vector3? NormalCamera, double? HeightMeters, Matrix4x4? GravityFromCamera, bool Fresh, bool Usable, double StaleSeconds, int Inliers, int Candidates);
