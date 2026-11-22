namespace BeckerBearings.App.Services.Mapping;

/// <summary>
/// Typed, portable counterpart to cloud-scan's <c>config.json</c> map section.
/// Defaults deliberately preserve the proven Python tuning.
/// </summary>
public sealed record MappingOptions
{
    public double SizeMeters { get; init; } = 6.0;
    public double CellMeters { get; init; } = .05;
    public double RecenterMarginMeters { get; init; } = 1.5;
    public int SampleStride { get; init; } = 3;
    public double MinRangeMeters { get; init; } = .5;
    public double MaxRangeMeters { get; init; } = 3.0;
    public double FarHintRangeMeters { get; init; } = 3.5;
    public double DisparityNoisePixels { get; init; } = .35;
    public double FloorImageStartFraction { get; init; } = .5;
    public double FloorMinRangeMeters { get; init; } = .5;
    public double FloorMaxRangeMeters { get; init; } = 2.5;
    public int FloorRansacSamples { get; init; } = 1200;
    public int FloorIterations { get; init; } = 160;
    public double FloorInlierMeters { get; init; } = .04;
    public int FloorMinInliers { get; init; } = 100;
    public double FloorMinFraction { get; init; } = .20;
    public double FloorMaxTiltDegrees { get; init; } = 40;
    public double FloorMaxAbsoluteTiltDegrees { get; init; } = 50;
    public double FloorMinHeightMeters { get; init; } = 1.0;
    public double FloorMaxHeightMeters { get; init; } = 2.1;
    public double FloorSmoothing { get; init; } = .35;
    public double FloorStaleSeconds { get; init; } = 1.0;
    public double FloorHeightToleranceMeters { get; init; } = .05;
    public double ObstacleMinHeightMeters { get; init; } = .08;
    public double ObstacleMaxHeightMeters { get; init; } = 2.0;
    public double ObstacleSigmaMultiplier { get; init; } = 2.0;
    public double ObstacleEvidenceRequired { get; init; } = 2.5;
    public double ObstacleEvidenceCap { get; init; } = 6;
    public double FloorEvidenceRequired { get; init; } = 1;
    public double FloorEvidencePerFrame { get; init; } = 1;
    public double FloorEvidenceCap { get; init; } = 5;
    public int PersistenceFrames { get; init; } = 5;
    public int PersistenceRequired { get; init; } = 3;
    public double EvidenceDecaySeconds { get; init; } = 5;
    public double EvidenceTtlSeconds { get; init; } = 10;
    public double FloorEvidenceDecaySeconds { get; init; } = 10;
    public double FloorEvidenceTtlSeconds { get; init; } = 12;
    public double ClearanceWidthMeters { get; init; } = .8;
    public double TraversalClearRadiusMeters { get; init; } = .30;
}
