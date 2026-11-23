using System.Runtime.InteropServices;
using CoreFoundation;
using Foundation;
using ObjCRuntime;

namespace FaceRecognition.Net.Maui;

public static partial class FaceSdk
{
    static readonly Queue<string> Events = new();
    static readonly object EventsGate = new();

    // The engine's license API is machineCode / activate: / license — the old getMachineCode,
    // setActivation: and getLicenseStatus are gone, and a missing selector is a runtime crash
    // the compiler cannot see. The names below match FaceEngine.swift in the iOS app.
    public static partial Task<string> GetMachineCodeAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => StringSend("machineCode")));

    public static partial Task<int> SetActivationAsync(string license) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            using var key = new NSString(license);
            return int_objc_msgSend_IntPtr(SdkClass(), Selector.GetHandle("activate:"), key.Handle);
        }));

    public static partial Task<int> InitAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => int_objc_msgSend(SdkClass(), Selector.GetHandle("initSDK"))));

    /// <summary>Built by FaceSdkCompat from the engine's License object, as the hosts expect.</summary>
    public static partial Task<string> GetLicenseStatusAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => BridgeString(FaceWorkerBridge_LicenseStatus)));

    public static partial Task<string> LastLicenseErrorAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => StringSend("lastLicenseError")));

    public static partial Task<string> DetectAsync(byte[] jpeg, int flags) =>
        SdkGate.RunAsync(() => Task.Run(() =>
            BridgeString(() => FaceWorkerBridge_Detect(jpeg, jpeg.Length, (ulong)(uint)flags))));

    public static partial Task<string> DetectWithTemplatesAsync(
        byte[] jpeg,
        int flags,
        int templates,
        bool sortBySize) =>
        SdkGate.RunAsync(() => Task.Run(() =>
            BridgeString(() => FaceWorkerBridge_DetectWithTemplates(
                jpeg, jpeg.Length, (ulong)(uint)flags, templates, sortBySize ? 1 : 0))));

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_Similarity")]
    private static extern float FaceWorkerBridge_Similarity(string feature1B64, string feature2B64);

    public static partial Task<double> SimilarityAsync(string feature1B64, string feature2B64) =>
        SdkGate.RunAsync(() => Task.Run(() => SimilarityOf(feature1B64, feature2B64)));

    public static partial Task<double[]> SimilarityManyAsync(
        string probeB64,
        IReadOnlyList<string> candidatesB64) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            // One gate entry for the whole search: the engine lock is the expensive part.
            var scores = new double[candidatesB64.Count];
            for (var i = 0; i < candidatesB64.Count; i++)
            {
                scores[i] = SimilarityOf(probeB64, candidatesB64[i]);
            }
            return scores;
        }));

    /// <summary>A template that will not decode scores -1 rather than throwing.</summary>
    private static double SimilarityOf(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return -1;
        }
        try
        {
            var value = FaceWorkerBridge_Similarity(a, b);
            return float.IsNaN(value) ? -1 : value;
        }
        catch
        {
            return -1;
        }
    }

    public static partial Task<byte[]?> CropFaceAsync(byte[] jpeg, FaceBox box) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            var b64 = BridgeString(() => FaceWorkerBridge_CropFace(
                jpeg,
                jpeg.Length,
                (int)Math.Round(box.X1),
                (int)Math.Round(box.Y1),
                (int)Math.Round(box.X2),
                (int)Math.Round(box.Y2)));
            return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64);
        }));

    /// <summary>
    /// Flutter/RN: real ObjC block via FaceWorkerBridge (hand-rolled C# blocks SIGSEGV on setHandler).
    /// </summary>
    public static partial Task<int> StartVideoWorkerAsync(float threshold) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            lock (EventsGate)
            {
                Events.Clear();
            }

            FaceWorkerBridge_SetCallback(Marshal.GetFunctionPointerForDelegate(OnNativeWorkerEvent));
            return FaceWorkerBridge_Start(threshold);
        }));

    public static partial Task StopVideoWorkerAsync() =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            FaceWorkerBridge_Stop();
            FaceWorkerBridge_SetCallback(IntPtr.Zero);
            lock (EventsGate)
            {
                Events.Clear();
            }
        }));

    /// <summary>
    /// The engine dropped the VideoWorker, so there is no
    /// syncVideoWorkerDatabaseWithFeatures:matchThreshold: to call any more. FaceSdkCompat
    /// emulates the worker, and this hands it the enrolled templates.
    /// </summary>
    public static partial Task<int> SyncDatabaseAsync(IReadOnlyList<string> featureBase64, float threshold) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            var lines = string.Join(
                "\n",
                featureBase64.Where(f => !string.IsNullOrWhiteSpace(f)));
            return FaceWorkerBridge_SyncWorker(lines, threshold);
        }));

    public static partial Task<int> AddFrameAsync(byte[] jpeg) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            var raw = BridgeString(() => FaceWorkerBridge_ApplyLiveFrame(
                jpeg, jpeg.Length, 0, LiveFramePrep.MaxEdge, 1));
            return raw.StartsWith("1|", StringComparison.Ordinal) ? Success : InitFailed;
        }));

    public static partial Task<LiveFrameResult> ApplyLiveFrameAsync(byte[] jpeg, int rotateDegrees, int maxEdge, bool feedWorker) =>
        Task.Run(() =>
        {
            var raw = BridgeString(() => FaceWorkerBridge_ApplyLiveFrame(
                jpeg,
                jpeg.Length,
                rotateDegrees,
                Math.Max(1, maxEdge),
                feedWorker ? 1 : 0));
            return ParseLiveResult(raw);
        });

    public static partial Task<LiveFrameResult> IngestLiveCameraFrameAsync(byte[] jpeg, bool frontCamera) =>
        Task.Run(() =>
        {
            // Probe size without SdkGate so Capture/Identify loops stay responsive.
            var (w0, h0) = CaptureLogic.JpegSize(jpeg);
            if (w0 <= 0 || h0 <= 0)
            {
                w0 = 720;
                h0 = 1280;
            }

            var plan = LiveFramePrep.PlanFor(frontCamera, w0, h0);
            var raw = BridgeString(() => FaceWorkerBridge_ApplyLiveFrame(
                jpeg,
                jpeg.Length,
                plan.RotateDegrees,
                plan.MaxEdge,
                1));
            return ParseLiveResult(raw);
        });

    public static partial Task<LiveFrameResult> ExportLastLiveFrameAsync() =>
        Task.Run(() =>
        {
            var b64 = BridgeString(FaceWorkerBridge_ExportLastLiveFrame);
            if (string.IsNullOrEmpty(b64))
            {
                return new LiveFrameResult(false, 0, 0, null);
            }

            var bytes = Convert.FromBase64String(b64);
            var size = BridgeString(FaceWorkerBridge_LastLiveSize);
            var parts = size.Split('|');
            var w = parts.Length > 0 && int.TryParse(parts[0], out var pw) ? pw : 0;
            var h = parts.Length > 1 && int.TryParse(parts[1], out var ph) ? ph : 0;
            if (w <= 0 || h <= 0)
            {
                (w, h) = CaptureLogic.JpegSize(bytes);
            }

            return new LiveFrameResult(true, w, h, bytes);
        });

    /// <summary>Lock-free vs SdkGate — native callback enqueues; UI loop must not wait on Detect.</summary>
    public static partial Task<string> PollEventAsync()
    {
        lock (EventsGate)
        {
            return Task.FromResult(Events.Count == 0 ? "" : Events.Dequeue());
        }
    }

    static LiveFrameResult ParseLiveResult(string raw)
    {
        var parts = (raw ?? "").Split('|');
        if (parts.Length < 3)
        {
            return new LiveFrameResult(false, 0, 0, null);
        }

        _ = int.TryParse(parts[1], out var w);
        _ = int.TryParse(parts[2], out var h);
        var ingested = parts[0] == "1";
        return new LiveFrameResult(ingested, w, h, null);
    }

    internal static void PushEvent(string json)
    {
        lock (EventsGate)
        {
            Events.Enqueue(json);
            while (Events.Count > 32)
            {
                Events.Dequeue();
            }
        }
    }

    static IntPtr SdkClass()
    {
        var handle = Class.GetHandle("FaceRecognitionSDK");
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "facerecognitionsdk.framework is not loaded. Copy the iOS frameworks to Platforms/iOS/Frameworks/.");
        }

        return handle;
    }

    static string StringSend(string selector) =>
        ReadString(IntPtr_objc_msgSend(SdkClass(), Selector.GetHandle(selector)));

    static string ReadString(IntPtr ptr) =>
        ptr == IntPtr.Zero ? "" : CFString.FromHandle(ptr) ?? "";

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern IntPtr IntPtr_objc_msgSend_IntPtr_byte_nuint(IntPtr receiver, IntPtr selector, IntPtr image, byte crop, nuint flags);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern IntPtr IntPtr_objc_msgSend_float_ret(IntPtr receiver, IntPtr selector, float arg1);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern int int_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern int int_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern int int_objc_msgSend_float(IntPtr receiver, IntPtr selector, float arg1);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern void void_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern void void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern void void_objc_msgSend_bool(IntPtr receiver, IntPtr selector, bool arg1);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate void FaceWorkerEventCallback(IntPtr utf8Json);

    // Keep the delegate rooted so the native bridge function pointer stays valid.
    static readonly FaceWorkerEventCallback OnNativeWorkerEvent = OnWorkerEventUtf8;

    [MonoPInvokeCallback(typeof(FaceWorkerEventCallback))]
    static void OnWorkerEventUtf8(IntPtr utf8Json)
    {
        if (utf8Json == IntPtr.Zero)
        {
            return;
        }

        var text = Marshal.PtrToStringUTF8(utf8Json) ?? "";
        if (text.Length > 0)
        {
            PushEvent(text);
        }
    }

    static string BridgeString(Func<IntPtr> call)
    {
        var ptr = call();
        try
        {
            return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(ptr) ?? "";
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                FaceWorkerBridge_FreeString(ptr);
            }
        }
    }

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_SetCallback")]
    static extern void FaceWorkerBridge_SetCallback(IntPtr callback);

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_Start")]
    static extern int FaceWorkerBridge_Start(float matchThreshold);

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_Stop")]
    static extern void FaceWorkerBridge_Stop();

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_DetectWithTemplates")]
    static extern IntPtr FaceWorkerBridge_DetectWithTemplates(
        byte[] bytes,
        int length,
        ulong flags,
        int templates,
        int sortBySize);

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_LicenseStatus")]
    static extern IntPtr FaceWorkerBridge_LicenseStatus();

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_SyncWorker")]
    static extern int FaceWorkerBridge_SyncWorker(string featuresB64Lines, float matchThreshold);

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_Detect")]
    static extern IntPtr FaceWorkerBridge_Detect(byte[] bytes, int length, ulong flags);

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_CropFace")]
    static extern IntPtr FaceWorkerBridge_CropFace(byte[] bytes, int length, int x1, int y1, int x2, int y2);

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_ApplyLiveFrame")]
    static extern IntPtr FaceWorkerBridge_ApplyLiveFrame(
        byte[] bytes,
        int length,
        float rotateDegrees,
        int maxEdge,
        int feedWorker);

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_ExportLastLiveFrame")]
    static extern IntPtr FaceWorkerBridge_ExportLastLiveFrame();

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_LastLiveSize")]
    static extern IntPtr FaceWorkerBridge_LastLiveSize();

    [DllImport("__Internal", EntryPoint = "FaceWorkerBridge_FreeString")]
    static extern void FaceWorkerBridge_FreeString(IntPtr value);
}
