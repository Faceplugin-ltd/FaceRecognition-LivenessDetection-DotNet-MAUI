namespace FaceRecognition.Net.Maui;

static class SdkGate
{
    static readonly SemaphoreSlim Lock = new(1, 1);

    public static Task<T> RunAsync<T>(Func<Task<T>> work) => RunCoreAsync(work);

    public static Task RunAsync(Func<Task> work) =>
        RunCoreAsync(async () =>
        {
            await work().ConfigureAwait(false);
            return 0;
        });

    static async Task<T> RunCoreAsync<T>(Func<Task<T>> work)
    {
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            Lock.Release();
        }
    }
}

public static partial class FaceSdk
{
    public const int Success = 0;
    public const int LicenseInvalid = 1;
    public const int LicenseExpired = 2;
    public const int NotActivated = 3;
    public const int InitFailed = 4;

    public const int DetectPose = 1 << 0;
    public const int DetectLandmarks = 1 << 1;
    public const int DetectAge = 1 << 2;
    public const int DetectGender = 1 << 3;
    public const int DetectEmotion = 1 << 4;
    public const int DetectMask = 1 << 5;
    public const int DetectQuality = 1 << 6;
    public const int DetectFaceQuality = 1 << 7;
    public const int DetectEyes = 1 << 8;
    public const int DetectLiveness = 1 << 9;
    public const int DetectGlasses = 1 << 11;
    public const int DetectLivenessAccurate = 1 << 16;

    public static partial Task<string> GetMachineCodeAsync();
    public static partial Task<int> SetActivationAsync(string license);
    public static partial Task<int> InitAsync();
    public static partial Task<string> GetLicenseStatusAsync();
    public static partial Task<string> LastLicenseErrorAsync();
    public static partial Task<string> DetectAsync(byte[] jpeg, int flags);

    /// <summary>
    /// Detection with a recognition template on the first <paramref name="templates"/> faces,
    /// read from the same open engine image — the pattern the native kits use. Asking for the
    /// templates afterwards costs a second full detect per face and can attribute a template
    /// to the wrong person when two faces are close together.
    /// </summary>
    public static partial Task<string> DetectWithTemplatesAsync(
        byte[] jpeg,
        int flags,
        int templates,
        bool sortBySize);

    /// <summary>Similarity of two base64 templates, 0..1. A bad template scores -1.</summary>
    public static partial Task<double> SimilarityAsync(string feature1B64, string feature2B64);

    /// <summary>
    /// Scores one probe against every candidate in a single call, so a 1:N search over a
    /// large gallery is not one bridge round trip per enrolled person.
    /// </summary>
    public static partial Task<double[]> SimilarityManyAsync(
        string probeB64,
        IReadOnlyList<string> candidatesB64);

    /// <summary>Flutter/RN cropFace — 1.4× square crop JPEG bytes, for the stored thumbnail.</summary>
    public static partial Task<byte[]?> CropFaceAsync(byte[] jpeg, FaceBox box);
    public static partial Task<int> StartVideoWorkerAsync(float threshold);
    public static partial Task StopVideoWorkerAsync();
    public static partial Task<int> SyncDatabaseAsync(IReadOnlyList<string> featureBase64, float threshold);
    public static partial Task<int> AddFrameAsync(byte[] jpeg);
    public static partial Task<string> PollEventAsync();

    /// <summary>Rotate + scale (max edge) then optionally feed VideoWorker; caches last live frame.</summary>
    public static partial Task<LiveFrameResult> ApplyLiveFrameAsync(byte[] jpeg, int rotateDegrees, int maxEdge, bool feedWorker);

    /// <summary>JPEG of the last prepared live frame (worker-aligned), for hold / final detect.</summary>
    public static partial Task<LiveFrameResult> ExportLastLiveFrameAsync();

    /// <summary>Flutter/RN ingestLiveCameraFrame — EXIF bake, planLiveFrame, applyLiveFrame(feedWorker: true).</summary>
    public static partial Task<LiveFrameResult> IngestLiveCameraFrameAsync(byte[] jpeg, bool frontCamera);

    public static string StatusLabel(int code) => code switch
    {
        Success => "Ready",
        LicenseInvalid => "Invalid license!",
        LicenseExpired => "License expired!",
        NotActivated => "No activated!",
        InitFailed => "Init error!",
        _ => $"Failed ({code})",
    };
}

public static class SdkSession
{
    public static bool Ready { get; private set; }
    public static bool Loading { get; private set; } = true;
    public static string Message { get; private set; } = "Loading native SDK…";
    public static string Machine { get; private set; } = "";

    public static async Task BootstrapAsync()
    {
        Loading = true;
        Ready = false;
        Message = "Loading native SDK…";
        try
        {
            await WaitForAndroidActivityAsync();
            SettingsStore.Load();
            Machine = await FaceSdk.GetMachineCodeAsync();
            var act = await FaceSdk.SetActivationAsync(LicenseKeys.Demo);
            if (act != FaceSdk.Success)
            {
                var detail = await FaceSdk.LastLicenseErrorAsync();
                Message = FaceSdk.StatusLabel(act) + (string.IsNullOrWhiteSpace(detail) ? "" : ": " + detail);
                return;
            }

            var code = await FaceSdk.InitAsync();
            if (code == FaceSdk.Success)
            {
                var label = FaceJson.LicenseLabel(await FaceSdk.GetLicenseStatusAsync());
                Message = string.IsNullOrWhiteSpace(label) ? "Ready" : "Ready · " + label;
                Ready = true;
            }
            else
            {
                // On the current engine an init failure is usually the license tier, and
                // lastLicenseError is the only thing that says which. The iOS app appends it
                // the same way.
                var detail = await FaceSdk.LastLicenseErrorAsync();
                Message = FaceSdk.StatusLabel(code)
                    + (string.IsNullOrWhiteSpace(detail) ? "" : ": " + detail);
            }
        }
        catch (Exception ex)
        {
            Message = "Init error: " + ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    static async Task WaitForAndroidActivityAsync()
    {
#if ANDROID
        for (var i = 0; i < 100 && Microsoft.Maui.ApplicationModel.Platform.CurrentActivity == null; i++)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
#endif
    }
}

static class PendingAttribute
{
    public static byte[]? Jpeg { get; set; }
    public static FaceBox? Box { get; set; }
}

static class PendingIdentifyResult
{
    public static byte[]? IdentifiedCropJpeg { get; set; }
    public static string? EnrolledThumbBase64 { get; set; }
    public static string PersonName { get; set; } = "";
    public static double Similarity { get; set; }
    public static FaceBox? Box { get; set; }
    public static IReadOnlyList<(double X, double Y)> CropLandmarks { get; set; } =
        Array.Empty<(double, double)>();
}
