using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>
/// Gravity-aligned rolling 2.5D evidence grid. It deliberately keeps evidence
/// separate from display state: unknown is never inferred to be free space.
/// </summary>
public sealed class ObstacleMap
{
    public const byte Unknown = 0, Free = 1, Obstacle = 2, Danger = 3, Remembered = 4;
    private readonly MappingOptions _options;
    private readonly int _side;
    private readonly float[] _obstacleEvidence;
    private readonly float[] _floorEvidence;
    private readonly float[] _dangerEvidence;
    private readonly float[] _minimumHeight;
    private readonly float[] _maximumHeight;
    private readonly double[] _lastObserved;
    private readonly double[] _lastFloorObserved;
    private readonly double[] _lastDangerObserved;
    private readonly byte[][] _obstacleHistory;
    private readonly Dictionary<(int X, int Z), RememberedObstacle> _memory = [];
    private int _historyIndex;
    private double? _lastUpdate;
    private double _now;

    public ObstacleMap(MappingOptions options)
    {
        _options = options; _side = (int)Math.Round(options.SizeMeters / options.CellMeters);
        if (_side < 2 || options.CellMeters <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        var count = _side * _side;
        _obstacleEvidence = new float[count]; _floorEvidence = new float[count]; _dangerEvidence = new float[count]; _minimumHeight = Enumerable.Repeat(float.PositiveInfinity, count).ToArray(); _maximumHeight = Enumerable.Repeat(float.NegativeInfinity, count).ToArray();
        _lastObserved = Enumerable.Repeat(double.NegativeInfinity, count).ToArray(); _lastFloorObserved = Enumerable.Repeat(double.NegativeInfinity, count).ToArray(); _lastDangerObserved = Enumerable.Repeat(double.NegativeInfinity, count).ToArray();
        _obstacleHistory = Enumerable.Range(0, options.PersistenceFrames).Select(_ => new byte[count]).ToArray();
        OriginXMeters = -_side * options.CellMeters / 2; OriginZMeters = OriginXMeters;
    }

    public double OriginXMeters { get; private set; }
    public double OriginZMeters { get; private set; }
    public Pose2D? Pose { get; private set; }
    public bool TrackingOk { get; private set; }
    public int IntegratedFrames { get; private set; }

    public MapIntegrationResult Integrate(StereoDepthFrame depth, FloorEstimate floor, Pose2D? pose, double timestampSeconds)
    {
        Decay(timestampSeconds); Pose = pose; TrackingOk = pose?.TrackingOk == true;
        if (!floor.Usable || floor.GravityFromCamera is not { } rotation || floor.HeightMeters is not { } cameraHeight || pose is not { TrackingOk: true } tracked)
            return MapIntegrationResult.NotIntegrated;
        Recenter(tracked); Array.Clear(_obstacleHistory[_historyIndex]); _historyIndex = (_historyIndex + 1) % _obstacleHistory.Length; IntegratedFrames++;
        var sampled = MapGeometry.SamplePoints(depth, _options.SampleStride, _options.MinRangeMeters, _options.FarHintRangeMeters, minimumConfidence: 1);
        var observedObstacleCells = new HashSet<int>(); var observedFloorCells = new HashSet<int>(); var dangerCells = new HashSet<int>(); var farHints = 0;
        foreach (var sample in sampled)
        {
            var gravity = MapGeometry.CameraToGravity(sample.CameraPoint, rotation, cameraHeight);
            var range = sample.CameraPoint.Z; var solid = sample.Confidence == 2;
            var noise = DisparityRangeNoise(depth, range); var minObstacleHeight = Math.Max(_options.ObstacleMinHeightMeters, _options.ObstacleSigmaMultiplier * noise);
            var inTrustedRange = range <= _options.MaxRangeMeters;
            if (!inTrustedRange) { if (solid && gravity.Y >= minObstacleHeight && gravity.Y <= _options.ObstacleMaxHeightMeters) farHints++; continue; }
            if (!TryCell(MapGeometry.GravityToWorldXZ(gravity, tracked), out var cell)) continue;
            if (solid && gravity.Y >= minObstacleHeight && gravity.Y <= _options.ObstacleMaxHeightMeters)
            {
                observedObstacleCells.Add(cell); _minimumHeight[cell] = Math.Min(_minimumHeight[cell], gravity.Y); _maximumHeight[cell] = Math.Max(_maximumHeight[cell], gravity.Y);
            }
            else if (Math.Abs(gravity.Y) <= _options.FloorHeightToleranceMeters) observedFloorCells.Add(cell);
            else if (solid && gravity.Y <= -Math.Max(.20, 2 * noise)) dangerCells.Add(cell);
        }
        // Obstacles win any contradictory evidence in one observation.
        observedFloorCells.ExceptWith(observedObstacleCells); dangerCells.ExceptWith(observedObstacleCells);
        var currentHistory = _obstacleHistory[(_historyIndex - 1 + _obstacleHistory.Length) % _obstacleHistory.Length];
        foreach (var cell in observedObstacleCells) currentHistory[cell] = 1;
        foreach (var cell in observedObstacleCells)
        {
            var persistence = _obstacleHistory.Sum(history => history[cell]);
            if (persistence >= _options.PersistenceRequired) _obstacleEvidence[cell] = (float)_options.ObstacleEvidenceCap;
            _lastObserved[cell] = timestampSeconds;
        }
        foreach (var cell in observedFloorCells)
        {
            _floorEvidence[cell] = Math.Min((float)_options.FloorEvidenceCap, _floorEvidence[cell] + (float)_options.FloorEvidencePerFrame);
            _obstacleEvidence[cell] = Math.Max(0, _obstacleEvidence[cell] - .08f); _lastFloorObserved[cell] = timestampSeconds; _lastObserved[cell] = timestampSeconds;
        }
        foreach (var cell in dangerCells) { _dangerEvidence[cell] = Math.Min((float)_options.ObstacleEvidenceCap, _dangerEvidence[cell] + 1); _lastDangerObserved[cell] = timestampSeconds; _lastObserved[cell] = timestampSeconds; }
        ClearTraversed(tracked, observedObstacleCells);
        RememberConfirmed(timestampSeconds);
        return new MapIntegrationResult(true, observedObstacleCells.Count, observedFloorCells.Count, dangerCells.Count, farHints);
    }

    public byte[] StateGrid()
    {
        var state = new byte[_side * _side];
        for (var index = 0; index < state.Length; index++)
        {
            var obstacleIsFresh = _now - _lastObserved[index] <= _options.EvidenceTtlSeconds;
            var floorIsFresh = _now - _lastFloorObserved[index] <= _options.FloorEvidenceTtlSeconds;
            var heightSupported = _maximumHeight[index] >= .30f || _maximumHeight[index] - _minimumHeight[index] >= .10f;
            var dangerIsFresh = _now - _lastDangerObserved[index] <= 2.5 && _dangerEvidence[index] >= _options.ObstacleEvidenceRequired;
            if (dangerIsFresh) state[index] = Danger;
            else if (obstacleIsFresh && _obstacleEvidence[index] >= _options.ObstacleEvidenceRequired && heightSupported) state[index] = Obstacle;
            else if (floorIsFresh && _floorEvidence[index] >= _options.FloorEvidenceRequired && _obstacleEvidence[index] < .5) state[index] = Free;
        }
        foreach (var (world, _) in _memory)
        {
            var x = world.X - (int)Math.Round(OriginXMeters / _options.CellMeters); var z = world.Z - (int)Math.Round(OriginZMeters / _options.CellMeters);
            if (x >= 0 && x < _side && z >= 0 && z < _side && state[z * _side + x] == Unknown) state[z * _side + x] = Remembered;
        }
        return state;
    }

    public string CellState(double xMeters, double zMeters)
    {
        if (!TryCell(xMeters, zMeters, out var cell)) return "unknown";
        return StateGrid()[cell] switch { Free => "free", Obstacle => "obstacle", Remembered => "remembered_obstacle", _ => "unknown" };
    }

    public double? NearestObstacle(double directionDegrees = 0, double widthDegrees = 20)
    {
        if (Pose is not { } pose) return null;
        var states = StateGrid(); double? nearest = null;
        for (var cell = 0; cell < states.Length; cell++) if (states[cell] == Obstacle)
        {
            var (x, z) = CellCenter(cell); var dx = x - pose.XMeters; var dz = z - pose.ZMeters; var bearing = NormalizeDegrees(Math.Atan2(dx, dz) * 180 / Math.PI - pose.YawRadians * 180 / Math.PI);
            if (Math.Abs(NormalizeDegrees(bearing - directionDegrees)) <= widthDegrees / 2) nearest = Math.Min(nearest ?? double.PositiveInfinity, Math.Sqrt(dx * dx + dz * dz));
        }
        return nearest;
    }

    public IReadOnlyDictionary<string, double?> ClearanceSectors() => new Dictionary<string, double?> { ["left"] = NearestObstacle(-45), ["front"] = NearestObstacle(), ["right"] = NearestObstacle(45) };

    private double DisparityRangeNoise(StereoDepthFrame depth, float range) => Math.Abs(depth.Q[14]) < 1e-9 ? 0 : _options.DisparityNoisePixels * range * range / Math.Abs(depth.Q[11] / depth.Q[14]);
    private void Decay(double timestamp)
    {
        if (_lastUpdate is { } last)
        {
            if (timestamp < last - 1e-6) throw new ArgumentException("Map timestamps must be monotonic.", nameof(timestamp));
            var delta = Math.Max(0, timestamp - last); var obstacleFactor = (float)Math.Exp(-delta / _options.EvidenceDecaySeconds); var floorFactor = (float)Math.Exp(-delta / _options.FloorEvidenceDecaySeconds);
            for (var i = 0; i < _obstacleEvidence.Length; i++) { _obstacleEvidence[i] *= obstacleFactor; _floorEvidence[i] *= floorFactor; _dangerEvidence[i] *= obstacleFactor; }
        }
        _lastUpdate = timestamp; _now = timestamp;
    }

    private void ClearTraversed(Pose2D pose, HashSet<int> protectedCells)
    {
        var squared = _options.TraversalClearRadiusMeters * _options.TraversalClearRadiusMeters;
        for (var cell = 0; cell < _obstacleEvidence.Length; cell++)
        {
            if (protectedCells.Contains(cell)) continue; var (x, z) = CellCenter(cell);
            if ((x - pose.XMeters) * (x - pose.XMeters) + (z - pose.ZMeters) * (z - pose.ZMeters) > squared) continue;
            _obstacleEvidence[cell] = 0; _dangerEvidence[cell] = 0; _minimumHeight[cell] = float.PositiveInfinity; _maximumHeight[cell] = float.NegativeInfinity; _memory.Remove(WorldCell(cell));
        }
    }

    private void RememberConfirmed(double timestamp)
    {
        foreach (var cell in Enumerable.Range(0, _obstacleEvidence.Length).Where(cell => _obstacleEvidence[cell] >= _options.ObstacleEvidenceRequired && (_maximumHeight[cell] >= .30f || _maximumHeight[cell] - _minimumHeight[cell] >= .10f)))
            _memory[WorldCell(cell)] = new RememberedObstacle(_minimumHeight[cell], _maximumHeight[cell], timestamp);
    }

    private void Recenter(Pose2D pose)
    {
        var centerX = OriginXMeters + _options.SizeMeters / 2; var centerZ = OriginZMeters + _options.SizeMeters / 2;
        if (Math.Abs(pose.XMeters - centerX) <= _options.SizeMeters / 2 - _options.RecenterMarginMeters && Math.Abs(pose.ZMeters - centerZ) <= _options.SizeMeters / 2 - _options.RecenterMarginMeters) return;
        // A moved grid must never reinterpret old array evidence at a new world position.
        Array.Clear(_obstacleEvidence); Array.Clear(_floorEvidence); Array.Clear(_dangerEvidence); Array.Fill(_minimumHeight, float.PositiveInfinity); Array.Fill(_maximumHeight, float.NegativeInfinity); Array.Fill(_lastObserved, double.NegativeInfinity); Array.Fill(_lastFloorObserved, double.NegativeInfinity); Array.Fill(_lastDangerObserved, double.NegativeInfinity); foreach (var history in _obstacleHistory) Array.Clear(history);
        OriginXMeters = pose.XMeters - _options.SizeMeters / 2; OriginZMeters = pose.ZMeters - _options.SizeMeters / 2;
    }

    private bool TryCell(System.Numerics.Vector2 world, out int cell) => TryCell(world.X, world.Y, out cell);
    private bool TryCell(double x, double z, out int cell)
    {
        var ix = (int)Math.Floor((x - OriginXMeters) / _options.CellMeters); var iz = (int)Math.Floor((z - OriginZMeters) / _options.CellMeters);
        if (ix < 0 || ix >= _side || iz < 0 || iz >= _side) { cell = -1; return false; } cell = iz * _side + ix; return true;
    }
    private (double X, double Z) CellCenter(int cell) => (OriginXMeters + (cell % _side + .5) * _options.CellMeters, OriginZMeters + (cell / _side + .5) * _options.CellMeters);
    private (int X, int Z) WorldCell(int cell) => ((int)Math.Round(OriginXMeters / _options.CellMeters) + cell % _side, (int)Math.Round(OriginZMeters / _options.CellMeters) + cell / _side);
    private static double NormalizeDegrees(double degrees) => (degrees + 180) % 360 - 180;
    private sealed record RememberedObstacle(float MinimumHeight, float MaximumHeight, double LastSeen);
}

public sealed record MapIntegrationResult(bool Integrated, int ObstacleCells, int FloorCells, int DangerCells, int FarHints)
{
    public static readonly MapIntegrationResult NotIntegrated = new(false, 0, 0, 0, 0);
}
