using System.Diagnostics;
using System.Text.Json;

namespace BeckerBearings.App.Services.Mapping;

/// <summary>Constant-memory named timers, equivalent to cloud-scan/module/benchmark.py.</summary>
public sealed class Benchmark
{
    private readonly Dictionary<string, Statistics> _statistics = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _running = new(StringComparer.Ordinal);

    public Benchmark Setup(params string[] names)
    {
        if (_running.Count != 0) throw new InvalidOperationException("Stop running timers before resetting benchmarks.");
        if (names.Length == 0 || names.Any(string.IsNullOrWhiteSpace) || names.Distinct().Count() != names.Length)
            throw new ArgumentException("Timer names must be nonempty and unique.", nameof(names));
        _statistics.Clear();
        foreach (var name in names) _statistics[name] = new Statistics();
        return this;
    }

    public void Start(string name)
    {
        if (!_statistics.ContainsKey(name)) throw new KeyNotFoundException($"Undeclared benchmark '{name}'.");
        if (!_running.TryAdd(name, Stopwatch.GetTimestamp())) throw new InvalidOperationException($"Benchmark '{name}' is already running.");
    }

    public double Stop(string name)
    {
        if (!_running.Remove(name, out var started)) throw new InvalidOperationException($"Benchmark '{name}' is not running.");
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _statistics[name].Add(milliseconds);
        return milliseconds;
    }

    public IDisposable Measure(string name) => new Scope(this, name);

    public IReadOnlyDictionary<string, BenchmarkResult> Results() => _statistics.ToDictionary(
        pair => pair.Key, pair => pair.Value.Result(), StringComparer.Ordinal);

    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, Results(), cancellationToken: cancellationToken);
    }

    private sealed class Scope : IDisposable
    {
        private Benchmark? _benchmark;
        public Scope(Benchmark benchmark, string name) { _benchmark = benchmark; benchmark.Start(name); Name = name; }
        private string Name { get; }
        public void Dispose() => Interlocked.Exchange(ref _benchmark, null)?.Stop(Name);
    }

    private sealed class Statistics
    {
        private int _count;
        private double _total;
        private double _min = double.PositiveInfinity;
        private double _max = double.NegativeInfinity;
        public void Add(double elapsed) { _count++; _total += elapsed; _min = Math.Min(_min, elapsed); _max = Math.Max(_max, elapsed); }
        public BenchmarkResult Result() => new(_count, _total, _count == 0 ? null : _min, _count == 0 ? null : _max, _count == 0 ? null : _total / _count);
    }
}

public sealed record BenchmarkResult(int Count, double TotalMilliseconds, double? MinMilliseconds, double? MaxMilliseconds, double? MeanMilliseconds);
