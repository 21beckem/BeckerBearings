using System.Numerics;
using BeckerBearings.App.Services.Depth;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>Python-compatible rolling gravity-aligned obstacle evidence grid.</summary>
public sealed class ObstacleMap
{
    public const byte Unknown = 0, Free = 1, Obstacle = 2, Danger = 3, Remembered = 4;
    private readonly MappingOptions _o; private readonly int _side; private readonly int _count;
    private readonly float[] _hits, _floorHits, _dangerHits, _minHeight, _maxHeight;
    private readonly double[] _lastSeen, _lastFloor, _lastDanger, _lastTraversed;
    private readonly byte[][] _recentObstacles, _recentFloor; private int _historyIndex; private double? _lastUpdate; private double _now;
    private readonly Dictionary<(int X, int Z), RememberedObstacle> _memory = new();
    private readonly Dictionary<int, Dictionary<(int X, int Z), RememberedObstacle>> _memoryEpochs = new();
    private int _memoryEpoch;

    public ObstacleMap(MappingOptions options)
    {
        _o = options; _side = (int)Math.Round(options.SizeMeters / options.CellMeters); _count = _side * _side;
        if (_side < 2 || options.CellMeters <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        _hits = new float[_count]; _floorHits = new float[_count]; _dangerHits = new float[_count];
        _minHeight = Enumerable.Repeat(float.PositiveInfinity, _count).ToArray(); _maxHeight = Enumerable.Repeat(float.NegativeInfinity, _count).ToArray();
        _lastSeen = Enumerable.Repeat(double.NegativeInfinity, _count).ToArray(); _lastFloor = Enumerable.Repeat(double.NegativeInfinity, _count).ToArray(); _lastDanger = Enumerable.Repeat(double.NegativeInfinity, _count).ToArray(); _lastTraversed = Enumerable.Repeat(double.NegativeInfinity, _count).ToArray();
        _recentObstacles = Enumerable.Range(0, options.PersistenceFrames).Select(_ => new byte[_count]).ToArray(); _recentFloor = Enumerable.Range(0, options.PersistenceFrames).Select(_ => new byte[_count]).ToArray();
        OriginXMeters = OriginZMeters = -_side * options.CellMeters / 2;
        _memoryEpochs[0] = _memory;
    }
    public double OriginXMeters { get; private set; } public double OriginZMeters { get; private set; } public Pose2D? Pose { get; private set; } public bool TrackingOk { get; private set; } public int IntegratedFrames { get; private set; }
    public int RememberedCells => _memory.Count; public int SessionMemoryCells => _memoryEpochs.Values.Sum(m => m.Count);

    public void ResetLocal(int epoch)
    {
        var memories = _memoryEpochs.ToDictionary(pair => pair.Key, pair => pair.Value);
        Array.Clear(_hits); Array.Clear(_floorHits); Array.Clear(_dangerHits); Array.Fill(_minHeight, float.PositiveInfinity); Array.Fill(_maxHeight, float.NegativeInfinity); Array.Fill(_lastSeen, double.NegativeInfinity); Array.Fill(_lastFloor, double.NegativeInfinity); Array.Fill(_lastDanger, double.NegativeInfinity); Array.Fill(_lastTraversed, double.NegativeInfinity);
        foreach (var history in _recentObstacles) Array.Clear(history); foreach (var history in _recentFloor) Array.Clear(history);
        OriginXMeters = OriginZMeters = -_side * _o.CellMeters / 2; _memoryEpoch = epoch;
        _memoryEpochs.Clear(); foreach (var pair in memories) _memoryEpochs[pair.Key] = pair.Value;
        if (!_memoryEpochs.TryGetValue(epoch, out var current)) _memoryEpochs[epoch] = current = new();
        _memory.Clear(); foreach (var pair in current) _memory[pair.Key] = pair.Value;
    }

    public MapIntegrationResult Integrate(StereoDepthFrame depth, FloorEstimate floor, Pose2D? pose, double timestampSeconds)
    {
        Decay(timestampSeconds); Pose = pose; TrackingOk = pose?.TrackingOk == true;
        if (!floor.Usable || !floor.Fresh || floor.GravityFromCamera is not { } rotation || floor.HeightMeters is not { } height || pose is not { TrackingOk: true } tracked) return MapIntegrationResult.NotIntegrated;
        Recenter(tracked); Array.Clear(_recentObstacles[_historyIndex]); Array.Clear(_recentFloor[_historyIndex]); _historyIndex = (_historyIndex + 1) % _recentObstacles.Length; IntegratedFrames++;
        var samples = MapGeometry.SamplePoints(depth, _o.SampleStride, _o.MinRangeMeters, _o.FarHintRangeMeters, minimumConfidence: 1);
        var obstacles = new HashSet<int>(); var floors = new HashSet<int>(); var dangers = new HashSet<int>(); var floorCounts = new int[_count]; var weakFloors = new int[_count]; var farHints = 0;
        foreach (var sample in samples)
        {
            var gravity = MapGeometry.CameraToGravity(sample.CameraPoint, rotation, height); var range = sample.CameraPoint.Z; var sigma = DisparityRangeNoise(depth, range); var minH = Math.Max(_o.ObstacleMinHeightMeters, _o.ObstacleSigmaMultiplier * sigma); var solid = sample.Confidence == 2;
            if (range > _o.MaxRangeMeters) { if (solid && gravity.Y >= minH && gravity.Y <= _o.ObstacleMaxHeightMeters) farHints++; continue; }
            if (!TryCell(MapGeometry.GravityToWorldXZ(gravity, tracked), out var cell)) continue;
            if (solid && gravity.Y >= minH && gravity.Y <= _o.ObstacleMaxHeightMeters) { obstacles.Add(cell); _minHeight[cell] = Math.Min(_minHeight[cell], gravity.Y); _maxHeight[cell] = Math.Max(_maxHeight[cell], gravity.Y); }
            else if (Math.Abs(gravity.Y) <= _o.FloorHeightToleranceMeters) { floors.Add(cell); if (solid) floorCounts[cell]++; else weakFloors[cell]++; }
            else if (solid && gravity.Y <= -Math.Max(_o.HoleMinBelowMeters, _o.DangerSigmaMultiplier * sigma)) dangers.Add(cell);
        }
        floors.ExceptWith(obstacles); dangers.ExceptWith(obstacles);
        if (floorCounts.Count(v => v > 0) < _o.DangerMinFloorSupportCells) dangers.Clear();
        var slot = (_historyIndex - 1 + _recentObstacles.Length) % _recentObstacles.Length; foreach (var cell in obstacles) _recentObstacles[slot][cell] = 1;
        foreach (var cell in floors) { _recentFloor[slot][cell] = 1; var dense = floorCounts[cell] >= _o.FloorDenseMinSamples; _floorHits[cell] = Math.Min((float)_o.FloorEvidenceCap, _floorHits[cell] + (float)(dense ? _o.FloorEvidencePerFrame : floorCounts[cell] > 0 ? _o.FloorSparseEvidencePerFrame : _o.FloorWeakEvidencePerFrame)); _lastFloor[cell] = timestampSeconds; _lastSeen[cell] = timestampSeconds; if (floorCounts[cell] > 0) { _hits[cell] = Math.Max(0, _hits[cell] - .08f); _dangerHits[cell] *= .5f; } }
        foreach (var cell in obstacles) { if (_recentObstacles.Sum(h => h[cell]) >= _o.PersistenceRequired) _hits[cell] = (float)_o.ObstacleEvidenceCap; _floorHits[cell] *= .5f; _lastSeen[cell] = timestampSeconds; }
        foreach (var cell in dangers) { _dangerHits[cell] = Math.Min((float)_o.ObstacleEvidenceCap, _dangerHits[cell] + 1); _floorHits[cell] = 0; _lastDanger[cell] = timestampSeconds; _lastSeen[cell] = timestampSeconds; }
        for (var cell = 0; cell < _count; cell++) if (_recentFloor.Sum(h => h[cell]) >= _o.FloorOverrideRequired && _recentObstacles.Sum(h => h[cell]) == 0) { _hits[cell] = 0; _dangerHits[cell] = 0; _minHeight[cell] = float.PositiveInfinity; _maxHeight[cell] = float.NegativeInfinity; _memory.Remove(WorldCell(cell)); }
        var traversed = ClearTraversed(tracked, obstacles); var supported = obstacles.Where(cell => _maxHeight[cell] >= _o.ObstacleHighSurfaceMeters || _maxHeight[cell] - _minHeight[cell] >= _o.ObstacleMinVerticalSpanMeters).ToArray(); foreach (var cell in supported.Where(cell => _hits[cell] >= _o.ObstacleEvidenceRequired)) Remember(cell, timestampSeconds);
        return new MapIntegrationResult(true, obstacles.Count, floors.Count, dangers.Count, farHints);
    }

    public byte[] StateGrid()
    {
        var state = new byte[_count]; for (var cell = 0; cell < _count; cell++) { var obstacle = _now - _lastSeen[cell] <= _o.EvidenceTtlSeconds && _hits[cell] >= _o.ObstacleEvidenceRequired && (_maxHeight[cell] >= _o.ObstacleHighSurfaceMeters || _maxHeight[cell] - _minHeight[cell] >= _o.ObstacleMinVerticalSpanMeters); var danger = _now - _lastDanger[cell] <= _o.DangerTtlSeconds && _dangerHits[cell] >= _o.DangerEvidenceRequired; var free = _now - _lastFloor[cell] <= _o.FloorEvidenceTtlSeconds && _floorHits[cell] >= _o.FloorEvidenceRequired && _hits[cell] < .5f && _recentObstacles.Sum(h => h[cell]) == 0 && !danger; state[cell] = danger ? Danger : obstacle ? Obstacle : free ? Free : Unknown; }
        var remembered = _o.RememberedObstacleMode == "tentative" ? Remembered : Obstacle; foreach (var key in _memory.Keys) { var ix = key.X - (int)Math.Round(OriginXMeters / _o.CellMeters); var iz = key.Z - (int)Math.Round(OriginZMeters / _o.CellMeters); if (ix >= 0 && ix < _side && iz >= 0 && iz < _side && state[iz * _side + ix] == Unknown) state[iz * _side + ix] = remembered; } return state;
    }
    public string CellState(double x, double z) => TryCell(x, z, out var cell) ? StateGrid()[cell] switch { Free => "free", Obstacle => "obstacle", Remembered => "remembered_obstacle", _ => "unknown" } : "unknown";
    public double? NearestObstacle(double directionDegrees = 0, double widthDegrees = 20) { if (Pose is not { } p) return null; var states = StateGrid(); double? nearest = null; for (var cell = 0; cell < _count; cell++) if (states[cell] == Obstacle) { var (x, z) = CellCenter(cell); var bearing = Normalize(Math.Atan2(x - p.XMeters, z - p.ZMeters) * 180 / Math.PI - p.YawRadians * 180 / Math.PI); if (Math.Abs(Normalize(bearing - directionDegrees)) <= widthDegrees / 2) nearest = Math.Min(nearest ?? double.PositiveInfinity, Math.Sqrt((x - p.XMeters) * (x - p.XMeters) + (z - p.ZMeters) * (z - p.ZMeters))); } return nearest; }
    public IReadOnlyDictionary<string, double?> ClearanceSectors() => new Dictionary<string, double?> { ["left"] = Corridor(-45), ["front"] = Corridor(0), ["right"] = Corridor(45) };
    public bool[] WalkedPathMask() => _lastTraversed.Select(t => _now - t <= _o.TraversalDisplayTtlSeconds).ToArray();
    private double? Corridor(double offset) { if (Pose is not { } p) return null; var states = StateGrid(); var a = p.YawRadians + offset * Math.PI / 180; double? best = null; for (var cell = 0; cell < _count; cell++) if (states[cell] == Obstacle) { var (x, z) = CellCenter(cell); var dx = x - p.XMeters; var dz = z - p.ZMeters; var forward = dx * Math.Sin(a) + dz * Math.Cos(a); var lateral = dx * Math.Cos(a) - dz * Math.Sin(a); if (forward > 0 && Math.Abs(lateral) <= _o.ClearanceWidthMeters / 2) best = Math.Min(best ?? double.PositiveInfinity, forward); } return best; }
    private void Remember(int cell, double timestamp) { var key = WorldCell(cell); var value = new RememberedObstacle(_minHeight[cell], _maxHeight[cell], timestamp); _memory[key] = value; _memoryEpochs[_memoryEpoch][key] = value; while (SessionMemoryCells > _o.MemoryMaxCells) { var oldest = _memoryEpochs.SelectMany(pair => pair.Value.Select(v => (Epoch: pair.Key, Key: v.Key, LastSeen: v.Value.LastSeen))).OrderBy(v => v.LastSeen).First(); _memoryEpochs[oldest.Epoch].Remove(oldest.Key); if (_memoryEpochs[oldest.Epoch] == _memory) _memory.Remove(oldest.Key); } }
    private (int X, int Z) WorldCell(int cell) => ((int)Math.Round(OriginXMeters / _o.CellMeters) + cell % _side, (int)Math.Round(OriginZMeters / _o.CellMeters) + cell / _side);
    private double DisparityRangeNoise(StereoDepthFrame depth, float range) => Math.Abs(depth.Q[14]) < 1e-9 ? 0 : _o.DisparityNoisePixels * range * range / Math.Abs(depth.Q[11] / depth.Q[14]);
    private void Decay(double t) { if (_lastUpdate is { } last) { if (t < last - 1e-6) throw new ArgumentException("Map timestamps must be monotonic.", nameof(t)); var dt = Math.Max(0, t - last); var f = (float)Math.Exp(-dt / _o.EvidenceDecaySeconds); var ff = (float)Math.Exp(-dt / _o.FloorEvidenceDecaySeconds); var df = (float)Math.Exp(-dt / _o.DangerDecaySeconds); for (var i = 0; i < _count; i++) { _hits[i] *= f; _floorHits[i] *= ff; _dangerHits[i] *= df; } } _lastUpdate = t; _now = t; }
    private int ClearTraversed(Pose2D p, HashSet<int> protectedCells) { var radius = _o.TraversalClearRadiusMeters; var cleared = 0; for (var cell = 0; cell < _count; cell++) { if (protectedCells.Contains(cell)) continue; var (x, z) = CellCenter(cell); if ((x - p.XMeters) * (x - p.XMeters) + (z - p.ZMeters) * (z - p.ZMeters) > radius * radius) continue; _hits[cell] = 0; _dangerHits[cell] = 0; _minHeight[cell] = float.PositiveInfinity; _maxHeight[cell] = float.NegativeInfinity; _lastTraversed[cell] = _now; _memory.Remove(WorldCell(cell)); cleared++; } return cleared; }
    private void Recenter(Pose2D p) { var cx = (p.XMeters - OriginXMeters) / _o.CellMeters; var cz = (p.ZMeters - OriginZMeters) / _o.CellMeters; var limit = _o.RecenterMarginMeters / _o.CellMeters; var dx = cx < limit || cx > _side - limit ? (int)Math.Round(cx - _side / 2) : 0; var dz = cz < limit || cz > _side - limit ? (int)Math.Round(cz - _side / 2) : 0; if (dx == 0 && dz == 0) return; Shift(_hits, dx, dz, 0); Shift(_floorHits, dx, dz, 0); Shift(_dangerHits, dx, dz, 0); Shift(_minHeight, dx, dz, float.PositiveInfinity); Shift(_maxHeight, dx, dz, float.NegativeInfinity); Shift(_lastSeen, dx, dz, double.NegativeInfinity); Shift(_lastFloor, dx, dz, double.NegativeInfinity); Shift(_lastDanger, dx, dz, double.NegativeInfinity); foreach (var h in _recentObstacles) Shift(h, dx, dz, (byte)0); foreach (var h in _recentFloor) Shift(h, dx, dz, (byte)0); OriginXMeters += dx * _o.CellMeters; OriginZMeters += dz * _o.CellMeters; }
    private void Shift<T>(T[] source, int dx, int dz, T fill) { var copy = new T[_count]; for (var z = 0; z < _side; z++) for (var x = 0; x < _side; x++) { var sx = x + dx; var sz = z + dz; copy[z * _side + x] = sx >= 0 && sx < _side && sz >= 0 && sz < _side ? source[sz * _side + sx] : fill; } Array.Copy(copy, source, _count); }
    private bool TryCell(Vector2 p, out int cell) => TryCell(p.X, p.Y, out cell); private bool TryCell(double x, double z, out int cell) { var ix = (int)Math.Floor((x - OriginXMeters) / _o.CellMeters); var iz = (int)Math.Floor((z - OriginZMeters) / _o.CellMeters); if (ix < 0 || ix >= _side || iz < 0 || iz >= _side) { cell = -1; return false; } cell = iz * _side + ix; return true; }
    private (double X, double Z) CellCenter(int cell) => (OriginXMeters + (cell % _side + .5) * _o.CellMeters, OriginZMeters + (cell / _side + .5) * _o.CellMeters); private static double Normalize(double d) => (d + 180) % 360 - 180;
    private readonly record struct RememberedObstacle(float MinimumHeight, float MaximumHeight, double LastSeen);
}

public sealed record MapIntegrationResult(bool Integrated, int ObstacleCells, int FloorCells, int DangerCells, int FarHints)
{ public static readonly MapIntegrationResult NotIntegrated = new(false, 0, 0, 0, 0); }
