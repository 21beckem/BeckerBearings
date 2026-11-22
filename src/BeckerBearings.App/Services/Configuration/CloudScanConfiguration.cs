using System.Text.Json;
using System.Text.Json.Serialization;
using BeckerBearings.App.Services.Mapping;

namespace BeckerBearings.App.Services.Configuration;

/// <summary>Loads cloud-scan compatible JSON without exposing file paths to UI code.</summary>
public sealed record CloudScanConfiguration
{
    [JsonPropertyName("calibration")] public string Calibration { get; init; } = "calibration.json";
    [JsonPropertyName("camera")] public CameraConfiguration Camera { get; init; } = new();
    [JsonPropertyName("map")] public MapConfiguration Map { get; init; } = new();
    public MappingOptions ToMappingOptions() => Map.ToOptions();
}

public sealed record CameraConfiguration
{
    [JsonPropertyName("width")] public int Width { get; init; } = 1280;
    [JsonPropertyName("height")] public int Height { get; init; } = 480;
    [JsonPropertyName("rotate_180")] public bool Rotate180 { get; init; } = true;
    [JsonPropertyName("right_eye_delay_frames")] public int RightEyeDelayFrames { get; init; }
}

public sealed record MapConfiguration
{
    [JsonPropertyName("size_m")] public double SizeMeters { get; init; } = 6;
    [JsonPropertyName("cell_m")] public double CellMeters { get; init; } = .05;
    [JsonPropertyName("recenter_margin_m")] public double RecenterMarginMeters { get; init; } = 1.5;
    [JsonPropertyName("sample_stride")] public int SampleStride { get; init; } = 3;
    [JsonPropertyName("min_range_m")] public double MinRangeMeters { get; init; } = .5;
    [JsonPropertyName("max_range_m")] public double MaxRangeMeters { get; init; } = 3;
    [JsonPropertyName("far_hint_range_m")] public double FarHintRangeMeters { get; init; } = 3.5;
    [JsonPropertyName("disparity_noise_px")] public double DisparityNoisePixels { get; init; } = .35;
    [JsonPropertyName("floor_image_start_fraction")] public double FloorImageStartFraction { get; init; } = .5;
    [JsonPropertyName("floor_min_range_m")] public double FloorMinRangeMeters { get; init; } = .5;
    [JsonPropertyName("floor_max_range_m")] public double FloorMaxRangeMeters { get; init; } = 2.5;
    [JsonPropertyName("floor_ransac_samples")] public int FloorRansacSamples { get; init; } = 1200;
    [JsonPropertyName("floor_iterations")] public int FloorIterations { get; init; } = 160;
    [JsonPropertyName("floor_inlier_m")] public double FloorInlierMeters { get; init; } = .04;
    [JsonPropertyName("floor_min_inliers")] public int FloorMinInliers { get; init; } = 100;
    [JsonPropertyName("floor_min_fraction")] public double FloorMinFraction { get; init; } = .2;
    [JsonPropertyName("floor_max_tilt_deg")] public double FloorMaxTiltDegrees { get; init; } = 40;
    [JsonPropertyName("floor_max_absolute_tilt_deg")] public double FloorMaxAbsoluteTiltDegrees { get; init; } = 50;
    [JsonPropertyName("floor_min_height_m")] public double FloorMinHeightMeters { get; init; } = 1;
    [JsonPropertyName("floor_max_height_m")] public double FloorMaxHeightMeters { get; init; } = 2.1;
    [JsonPropertyName("floor_smoothing")] public double FloorSmoothing { get; init; } = .35;
    [JsonPropertyName("floor_stale_s")] public double FloorStaleSeconds { get; init; } = 1;
    [JsonPropertyName("floor_height_tol_m")] public double FloorHeightToleranceMeters { get; init; } = .05;
    [JsonPropertyName("obstacle_min_height_m")] public double ObstacleMinHeightMeters { get; init; } = .08;
    [JsonPropertyName("obstacle_max_height_m")] public double ObstacleMaxHeightMeters { get; init; } = 2;
    [JsonPropertyName("obstacle_sigma_multiplier")] public double ObstacleSigmaMultiplier { get; init; } = 2;
    [JsonPropertyName("obstacle_evidence_required")] public double ObstacleEvidenceRequired { get; init; } = 2.5;
    [JsonPropertyName("obstacle_evidence_cap")] public double ObstacleEvidenceCap { get; init; } = 6;
    [JsonPropertyName("floor_evidence_required")] public double FloorEvidenceRequired { get; init; } = 1;
    [JsonPropertyName("floor_evidence_per_frame")] public double FloorEvidencePerFrame { get; init; } = 1;
    [JsonPropertyName("floor_evidence_cap")] public double FloorEvidenceCap { get; init; } = 5;
    [JsonPropertyName("persistence_frames")] public int PersistenceFrames { get; init; } = 5;
    [JsonPropertyName("persistence_required")] public int PersistenceRequired { get; init; } = 3;
    [JsonPropertyName("evidence_decay_s")] public double EvidenceDecaySeconds { get; init; } = 5;
    [JsonPropertyName("evidence_ttl_s")] public double EvidenceTtlSeconds { get; init; } = 10;
    [JsonPropertyName("floor_evidence_decay_s")] public double FloorEvidenceDecaySeconds { get; init; } = 10;
    [JsonPropertyName("floor_evidence_ttl_s")] public double FloorEvidenceTtlSeconds { get; init; } = 12;
    [JsonPropertyName("clearance_width_m")] public double ClearanceWidthMeters { get; init; } = .8;
    [JsonPropertyName("traversal_clear_radius_m")] public double TraversalClearRadiusMeters { get; init; } = .3;

    public MappingOptions ToOptions() => new()
    {
        SizeMeters = SizeMeters, CellMeters = CellMeters, RecenterMarginMeters = RecenterMarginMeters, SampleStride = SampleStride, MinRangeMeters = MinRangeMeters, MaxRangeMeters = MaxRangeMeters, FarHintRangeMeters = FarHintRangeMeters, DisparityNoisePixels = DisparityNoisePixels, FloorImageStartFraction = FloorImageStartFraction, FloorMinRangeMeters = FloorMinRangeMeters, FloorMaxRangeMeters = FloorMaxRangeMeters, FloorRansacSamples = FloorRansacSamples, FloorIterations = FloorIterations, FloorInlierMeters = FloorInlierMeters, FloorMinInliers = FloorMinInliers, FloorMinFraction = FloorMinFraction, FloorMaxTiltDegrees = FloorMaxTiltDegrees, FloorMaxAbsoluteTiltDegrees = FloorMaxAbsoluteTiltDegrees, FloorMinHeightMeters = FloorMinHeightMeters, FloorMaxHeightMeters = FloorMaxHeightMeters, FloorSmoothing = FloorSmoothing, FloorStaleSeconds = FloorStaleSeconds, FloorHeightToleranceMeters = FloorHeightToleranceMeters, ObstacleMinHeightMeters = ObstacleMinHeightMeters, ObstacleMaxHeightMeters = ObstacleMaxHeightMeters, ObstacleSigmaMultiplier = ObstacleSigmaMultiplier, ObstacleEvidenceRequired = ObstacleEvidenceRequired, ObstacleEvidenceCap = ObstacleEvidenceCap, FloorEvidenceRequired = FloorEvidenceRequired, FloorEvidencePerFrame = FloorEvidencePerFrame, FloorEvidenceCap = FloorEvidenceCap, PersistenceFrames = PersistenceFrames, PersistenceRequired = PersistenceRequired, EvidenceDecaySeconds = EvidenceDecaySeconds, EvidenceTtlSeconds = EvidenceTtlSeconds, FloorEvidenceDecaySeconds = FloorEvidenceDecaySeconds, FloorEvidenceTtlSeconds = FloorEvidenceTtlSeconds, ClearanceWidthMeters = ClearanceWidthMeters, TraversalClearRadiusMeters = TraversalClearRadiusMeters
    };
}

public static class CloudScanConfigurationLoader
{
    public static async Task<CloudScanConfiguration> LoadAsync(Stream json, CancellationToken cancellationToken = default) =>
        await JsonSerializer.DeserializeAsync<CloudScanConfiguration>(json, cancellationToken: cancellationToken) ?? throw new InvalidDataException("The configuration JSON was empty.");
}
