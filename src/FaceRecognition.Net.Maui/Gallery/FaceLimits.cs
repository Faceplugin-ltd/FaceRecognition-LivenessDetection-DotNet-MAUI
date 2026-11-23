namespace FaceRecognition.Net.Maui;

/// <summary>
/// Every threshold the gallery checks against. Built from <see cref="SettingsStore.Current"/>
/// and passed in — the gallery reads no preferences of its own, so one screen cannot disagree
/// with another about what counts as a match.
/// </summary>
public readonly record struct FaceLimits(
    double Match,
    double Liveness,
    double Yaw,
    double Roll,
    double Pitch)
{
    private const double Epsilon = 0.0001;

    /// <summary>True when <paramref name="score"/> counts as a live face.</summary>
    public bool IsLive(double? score) =>
        score is { } s && double.IsFinite(s) && s + Epsilon >= Liveness;

    /// <summary>True when the head is within the limits of facing the camera.</summary>
    public bool PoseOk(double yaw, double roll, double pitch) =>
        Math.Abs(yaw) <= Yaw && Math.Abs(roll) <= Roll && Math.Abs(pitch) <= Pitch;

    /// <summary>True when <paramref name="similarity"/> counts as the same person.</summary>
    public bool Matches(double similarity) => similarity >= Match;
}

/// <summary>
/// The demo's view of <see cref="SettingsStore"/>: the limits the gallery takes, and the one
/// liveness rule every screen uses. Mirrors the Android app's <c>AppSettings</c>.
/// </summary>
public static class DemoSettings
{
    public const double MinThreshold = 0.70;
    public const double MaxThreshold = 0.90;

    /// <summary>Every threshold the gallery checks against, read from the saved settings.</summary>
    public static FaceLimits Limits
    {
        get
        {
            var s = SettingsStore.Current;
            return new FaceLimits(
                ClampThreshold(s.IdentifyThreshold),
                s.LivenessThreshold,
                s.YawThreshold,
                s.RollThreshold,
                s.PitchThreshold);
        }
    }

    /// <summary>Snapped to two decimals so the slider and the shown value never disagree.</summary>
    public static double ClampThreshold(double v) =>
        Math.Round(Math.Clamp(v, MinThreshold, MaxThreshold), 2);

    /// <summary>
    /// The one liveness rule (Identify, Attribute, Add person): live when the score is at or
    /// above the liveness threshold in Settings. The SDK's own Live/Fake label uses a fixed
    /// 0.5 and is ignored, so moving the slider actually changes the verdict.
    /// </summary>
    public static bool IsLive(double? score) => Limits.IsLive(score);

    /// <summary>Whether identify enforces the liveness threshold.</summary>
    public static bool LivenessOn
    {
        get => SettingsStore.Current.CheckLiveness;
        set => SettingsStore.Save(SettingsStore.Current with { CheckLiveness = value });
    }

    /// <summary>Whether the camera screens fire as soon as a good face holds still.</summary>
    public static bool AutoCapture
    {
        get => SettingsStore.Current.AutoCapture;
        set => SettingsStore.Save(SettingsStore.Current with { AutoCapture = value });
    }
}
