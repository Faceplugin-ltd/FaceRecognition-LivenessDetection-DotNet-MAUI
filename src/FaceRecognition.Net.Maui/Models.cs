using System.Text.Json;

namespace FaceRecognition.Net.Maui;

public sealed class FaceBox
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public double? Yaw { get; init; }
    public double? Pitch { get; init; }
    public double? Roll { get; init; }
    public double? Liveness { get; init; }
    public string? LivenessLabel { get; init; }
    public string? GenderLabel { get; init; }
    public string? EmotionLabel { get; init; }
    public string? MaskLabel { get; init; }
    public string? EyesLeftLabel { get; init; }
    public string? EyesRightLabel { get; init; }
    public string? GlassesLabel { get; init; }
    public string? SunglassesLabel { get; init; }
    public string? OcclusionLabel { get; init; }
    public double? LeftEyeClosed { get; init; }
    public double? RightEyeClosed { get; init; }
    public double? Age { get; init; }
    public double? FaceQuality { get; init; }
    public double? FaceLuminance { get; init; }
    public string? QualityLabel { get; init; }
    public IReadOnlyList<double> Landmarks { get; init; } = Array.Empty<double>();

    /// <summary>
    /// The recognition template, when it was asked for on the same detect call. Base64, or
    /// null when it was not requested or the engine could not read this face.
    /// </summary>
    public string? Template { get; init; }

    /// <summary>How long the engine took over this face's template, in milliseconds.</summary>
    public double TemplateMs { get; init; }
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>();

    public double Width => X2 - X1;
    public double Height => Y2 - Y1;

    public FaceBox WithLiveness(double? liveness, string? label) =>
        Clone(liveness: liveness, livenessLabel: label);

    public FaceBox WithEyes(
        string? leftLabel,
        string? rightLabel,
        double? leftClosed,
        double? rightClosed) =>
        Clone(
            eyesLeft: leftLabel,
            eyesRight: rightLabel,
            leftClosed: leftClosed,
            rightClosed: rightClosed);

    FaceBox Clone(
        double? liveness = null,
        string? livenessLabel = null,
        string? eyesLeft = null,
        string? eyesRight = null,
        double? leftClosed = null,
        double? rightClosed = null) =>
        new()
        {
            X1 = X1,
            Y1 = Y1,
            X2 = X2,
            Y2 = Y2,
            Yaw = Yaw,
            Pitch = Pitch,
            Roll = Roll,
            Liveness = liveness ?? Liveness,
            LivenessLabel = livenessLabel ?? LivenessLabel,
            GenderLabel = GenderLabel,
            EmotionLabel = EmotionLabel,
            MaskLabel = MaskLabel,
            EyesLeftLabel = eyesLeft ?? EyesLeftLabel,
            EyesRightLabel = eyesRight ?? EyesRightLabel,
            GlassesLabel = GlassesLabel,
            SunglassesLabel = SunglassesLabel,
            OcclusionLabel = OcclusionLabel,
            LeftEyeClosed = leftClosed ?? LeftEyeClosed,
            RightEyeClosed = rightClosed ?? RightEyeClosed,
            Age = Age,
            FaceQuality = FaceQuality,
            FaceLuminance = FaceLuminance,
            QualityLabel = QualityLabel,
            Template = Template,
            TemplateMs = TemplateMs,
            Landmarks = Landmarks,
            Attributes = Attributes,
        };
}

/// <summary>
/// The saved demo settings. Defaults are the engine operating points, matching the Android
/// and iOS apps: match 0.80, liveness 0.5, head pose plus or minus 20 degrees, with the
/// liveness gate off until the user turns it on.
/// </summary>
public sealed record AppSettings
{
    public bool FrontCamera { get; init; } = true;
    public double LivenessThreshold { get; init; } = 0.5;
    public int LivenessLevel { get; init; }
    public double IdentifyThreshold { get; init; } = 0.80;
    public double YawThreshold { get; init; } = 20;
    public double RollThreshold { get; init; } = 20;
    public double PitchThreshold { get; init; } = 20;
    public double EyeCloseThreshold { get; init; } = 0.5;

    /// <summary>Whether identify rejects a probe that fails the liveness threshold.</summary>
    public bool CheckLiveness { get; init; }

    /// <summary>Whether the camera screens fire once a good face has held still.</summary>
    public bool AutoCapture { get; init; }

    /// <summary>Enroll straight away when every check passes, skipping the capture review.</summary>
    public bool SkipReview { get; init; }

    public int CaptureFlags
    {
        get
        {
            var flags = FaceSdk.DetectPose | FaceSdk.DetectLandmarks | FaceSdk.DetectEyes
                | FaceSdk.DetectMask | FaceSdk.DetectLiveness;
            if (LivenessLevel == 0)
            {
                flags |= FaceSdk.DetectLivenessAccurate;
            }

            return flags;
        }
    }

    /// <summary>
    /// What enrollment needs from the engine: a box, a pose and the landmarks the crop uses,
    /// plus quality. The attribute estimators cost a pass each and nothing in the enroll path
    /// reads them — the capture review already has them from the live session.
    /// </summary>
    public int EnrollFlags =>
        FaceSdk.DetectPose | FaceSdk.DetectLandmarks
        | FaceSdk.DetectQuality | FaceSdk.DetectFaceQuality;

    /// <summary>
    /// What 1:N identification needs: pose, landmarks and eye openness, and liveness only when
    /// the gate is on. Matches `detect(liveness, pose = true, eyes = true)` in the Android kit
    /// and the same param in the iOS kit.
    /// </summary>
    public int IdentifyFlags(bool livenessOn)
    {
        var flags = FaceSdk.DetectPose | FaceSdk.DetectLandmarks | FaceSdk.DetectEyes;
        if (livenessOn)
        {
            flags |= FaceSdk.DetectLiveness;
            if (LivenessLevel == 0)
            {
                flags |= FaceSdk.DetectLivenessAccurate;
            }
        }

        return flags;
    }

    /// <summary>Android FaceDetectionParam.allAttributes() + check_liveness_level.</summary>
    public int AttributeFlags
    {
        get
        {
            var flags = FaceSdk.DetectPose | FaceSdk.DetectLandmarks
                | FaceSdk.DetectAge | FaceSdk.DetectGender
                | FaceSdk.DetectEmotion | FaceSdk.DetectMask
                | FaceSdk.DetectQuality | FaceSdk.DetectFaceQuality
                | FaceSdk.DetectEyes | FaceSdk.DetectLiveness | FaceSdk.DetectGlasses;
            if (LivenessLevel == 0)
            {
                flags |= FaceSdk.DetectLivenessAccurate;
            }

            return flags;
        }
    }

    public int LivenessFlags
    {
        get
        {
            var flags = FaceSdk.DetectLiveness;
            if (LivenessLevel == 0)
            {
                flags |= FaceSdk.DetectLivenessAccurate;
            }

            return flags;
        }
    }
}

public static class SettingsStore
{
    const string Key = "face_settings_v1";

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        var json = Preferences.Get(Key, "");
        if (string.IsNullOrWhiteSpace(json))
        {
            Current = new AppSettings();
            return;
        }

        try
        {
            Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Current = settings;
        Preferences.Set(Key, JsonSerializer.Serialize(settings));
    }

    public static void RestoreDefaults() => Save(new AppSettings());
}
