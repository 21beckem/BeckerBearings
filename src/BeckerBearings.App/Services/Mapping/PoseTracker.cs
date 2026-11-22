using System.Numerics;
using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>
/// Depth-assisted sparse visual odometry module. The API is intentionally
/// independent of rendering and hardware so its matcher can be replaced by a
/// faster native OpenCV implementation without changing mapping callers.
/// </summary>
public sealed class PoseTracker
{
    private StereoDepthFrame? _previous;
    private double? _previousTimestamp;
    private Pose2D _pose = new();

    public TrackingResult Update(StereoDepthFrame current, double timestampSeconds)
    {
        if (_previous is null || _previousTimestamp is null)
        {
            _previous = current; _previousTimestamp = timestampSeconds; _pose = _pose with { TrackingOk = true };
            return new TrackingResult(_pose, true, 0, false);
        }
        var matches = Match(_previous, current);
        var elapsed = Math.Max(1e-3, timestampSeconds - _previousTimestamp.Value);
        if (matches.Count >= 8)
        {
            var deltas = matches.Select(match => match.Previous - match.Current).ToArray();
            var translation = new Vector3(Median(deltas.Select(d => d.X)), Median(deltas.Select(d => d.Y)), Median(deltas.Select(d => d.Z)));
            var horizontalSpeed = Math.Sqrt(translation.X * translation.X + translation.Z * translation.Z) / elapsed;
            if (horizontalSpeed <= 1.5)
                _pose = _pose with { XMeters = _pose.XMeters + translation.X, ZMeters = _pose.ZMeters + translation.Z, TrackingOk = true };
            else _pose = _pose with { TrackingOk = false };
        }
        else _pose = _pose with { TrackingOk = false };
        _previous = current; _previousTimestamp = timestampSeconds;
        return new TrackingResult(_pose, _pose.TrackingOk, matches.Count, !_pose.TrackingOk);
    }

    public void Reset(Pose2D? pose = null) { _previous = null; _previousTimestamp = null; _pose = pose ?? new Pose2D(); }

    private static List<(Vector3 Previous, Vector3 Current)> Match(StereoDepthFrame previous, StereoDepthFrame current)
    {
        var matches = new List<(Vector3 Previous, Vector3 Current)>();
        const int search = 7, patch = 2;
        for (var row = 16; row < previous.Height - 16; row += 24)
        for (var column = 16; column < previous.Width - 16; column += 24)
        {
            var source = row * previous.Width + column;
            if (previous.Confidence[source] < 2 || previous.Disparity[source] <= 0 || !MapGeometry.TryReproject(previous.Q, column, row, previous.Disparity[source], out var priorPoint)) continue;
            var bestScore = int.MaxValue; var bestColumn = -1; var bestRow = -1;
            for (var y = row - search; y <= row + search; y++)
            for (var x = column - search; x <= column + search; x++)
            {
                var target = y * current.Width + x;
                if (current.Confidence[target] < 2 || current.Disparity[target] <= 0) continue;
                var score = PatchDifference(previous.Gray, current.Gray, previous.Width, column, row, x, y, patch);
                if (score < bestScore) { bestScore = score; bestColumn = x; bestRow = y; }
            }
            if (bestColumn < 0 || bestScore > 25 * (2 * patch + 1) * (2 * patch + 1)) continue;
            var targetIndex = bestRow * current.Width + bestColumn;
            if (MapGeometry.TryReproject(current.Q, bestColumn, bestRow, current.Disparity[targetIndex], out var currentPoint)) matches.Add((priorPoint, currentPoint));
        }
        return matches;
    }

    private static int PatchDifference(byte[] first, byte[] second, int width, int firstX, int firstY, int secondX, int secondY, int radius)
    {
        var score = 0;
        for (var y = -radius; y <= radius; y++) for (var x = -radius; x <= radius; x++) score += Math.Abs(first[(firstY + y) * width + firstX + x] - second[(secondY + y) * width + secondX + x]);
        return score;
    }
    private static float Median(IEnumerable<float> values) { var ordered = values.OrderBy(value => value).ToArray(); return ordered[ordered.Length / 2]; }
}

public sealed record TrackingResult(Pose2D Pose, bool TrackingOk, int Correspondences, bool RequestedMapReset);
