using System.Numerics;
using System.Runtime.InteropServices;
using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>UI-free wrapper around the OpenCV LK/PnP visual-odometry core.</summary>
public sealed class PoseTracker : IDisposable
{
    private readonly IntPtr _handle;
    private float[]? _previousFloor;
    private bool _disposed;
    public int MapEpoch { get; private set; }

    public PoseTracker(MappingOptions options)
    {
        _handle = gg_pose_create();
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("Could not create the native pose tracker.");
    }

    public TrackingResult Update(StereoDepthFrame current, FloorEstimate floor, double timestampSeconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var normal = floor.NormalCamera is { } n ? new[] { n.X, n.Y, n.Z } : new[] { 0f, -1f, 0f };
        var previous = _previousFloor ?? normal;
        var x = 0d; var z = 0d; var yaw = 0d; var ok = 0; var inliers = 0; var reset = 0;
        var code = gg_pose_update(_handle, current.Gray, current.Disparity, current.Confidence, current.Width, current.Height, current.Q, previous, normal, floor.Usable ? 1 : 0, timestampSeconds, ref x, ref z, ref yaw, ref ok, ref inliers, ref reset);
        if (code != 0) throw new InvalidOperationException(code == 2
            ? "Native pose tracking rejected the frame (OpenCV input/state error)."
            : $"Native pose tracking failed ({code}).");
        if (floor.Usable) _previousFloor = normal;
        if (reset != 0) MapEpoch++;
        return new TrackingResult(new Pose2D(x, z, yaw, ok != 0), ok != 0, inliers, reset != 0);
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; gg_pose_destroy(_handle);
    }

    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr gg_pose_create();
    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)] private static extern void gg_pose_destroy(IntPtr handle);
    [DllImport("BeckerBearings_vision", CallingConvention = CallingConvention.Cdecl)] private static extern int gg_pose_update(IntPtr handle, byte[] gray, float[] disparity, byte[] confidence, int width, int height, double[] q, float[] previousFloorNormal, float[] currentFloorNormal, int floorUsable, double timestamp, ref double x, ref double z, ref double yaw, ref int trackingOk, ref int inliers, ref int resetMap);
}

public sealed record TrackingResult(Pose2D Pose, bool TrackingOk, int Correspondences, bool RequestedMapReset);
