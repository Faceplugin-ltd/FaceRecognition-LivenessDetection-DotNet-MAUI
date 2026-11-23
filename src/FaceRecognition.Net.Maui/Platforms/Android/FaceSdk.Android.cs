using Android.Graphics;
using Android.Media;
using Android.Runtime;

namespace FaceRecognition.Net.Maui;

public static partial class FaceSdk
{
    const string Bridge = "com/faceplugin/facerecognition/maui/FaceBridge";

    public static partial Task<string> GetMachineCodeAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => CallString("machineCode", "(Landroid/content/Context;)Ljava/lang/String;", Ctx())));

    public static partial Task<int> SetActivationAsync(string license) =>
        SdkGate.RunAsync(() => Task.Run(() =>
            CallInt("activate", "(Landroid/content/Context;Ljava/lang/String;)I", Ctx(), new JValue(JNIEnv.NewString(license)))));

    public static partial Task<int> InitAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => CallInt("init", "(Landroid/content/Context;)I", Ctx())));

    public static partial Task<string> GetLicenseStatusAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => CallString("licenseStatus", "()Ljava/lang/String;")));

    public static partial Task<string> LastLicenseErrorAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => CallString("lastError", "()Ljava/lang/String;")));

    public static partial Task<string> DetectAsync(byte[] jpeg, int flags) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            using var bitmap = Decode(jpeg);
            return CallString("detect", "(Landroid/graphics/Bitmap;I)Ljava/lang/String;", new JValue(bitmap), new JValue(flags));
        }));

    public static partial Task<string> DetectWithTemplatesAsync(
        byte[] jpeg,
        int flags,
        int templates,
        bool sortBySize) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            using var bitmap = Decode(jpeg);
            return CallString(
                "detectWithTemplates",
                "(Landroid/graphics/Bitmap;IIZ)Ljava/lang/String;",
                new JValue(bitmap),
                new JValue(flags),
                new JValue(templates),
                new JValue(sortBySize));
        }));

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
        IntPtr left = IntPtr.Zero;
        IntPtr right = IntPtr.Zero;
        try
        {
            left = JNIEnv.NewArray(Convert.FromBase64String(a));
            right = JNIEnv.NewArray(Convert.FromBase64String(b));
            var value = CallFloat("similarity", "([B[B)F", new JValue(left), new JValue(right));
            return float.IsNaN(value) ? -1 : value;
        }
        catch
        {
            return -1;
        }
        finally
        {
            // Local refs are bounded per JNI frame; a 1:N search would exhaust them otherwise.
            if (left != IntPtr.Zero) JNIEnv.DeleteLocalRef(left);
            if (right != IntPtr.Zero) JNIEnv.DeleteLocalRef(right);
        }
    }

    static float CallFloat(string name, string sig, params JValue[] args)
    {
        try
        {
            var cls = Class();
            var method = JNIEnv.GetStaticMethodID(cls, name, sig);
            return JNIEnv.CallStaticFloatMethod(cls, method, args);
        }
        catch (Java.Lang.Throwable ex)
        {
            throw new InvalidOperationException(ex.Message ?? "Face Recognition SDK call failed.", ex);
        }
    }

    public static partial Task<byte[]?> CropFaceAsync(byte[] jpeg, FaceBox box) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            using var bitmap = Decode(jpeg);
            var b64 = CallString(
                "cropFaceBase64",
                "(Landroid/graphics/Bitmap;IIII)Ljava/lang/String;",
                new JValue(bitmap),
                new JValue((int)Math.Round(box.X1)),
                new JValue((int)Math.Round(box.Y1)),
                new JValue((int)Math.Round(box.X2)),
                new JValue((int)Math.Round(box.Y2)));
            return string.IsNullOrEmpty(b64) ? null : Convert.FromBase64String(b64);
        }));

    public static partial Task<int> StartVideoWorkerAsync(float threshold) =>
        SdkGate.RunAsync(() => Task.Run(() => CallInt("startVideoWorker", "(F)I", new JValue(threshold))));

    public static partial Task StopVideoWorkerAsync() =>
        SdkGate.RunAsync(() => Task.Run(() => CallVoid("stopVideoWorker", "()V")));

    public static partial Task<int> SyncDatabaseAsync(IReadOnlyList<string> featureBase64, float threshold) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            var packed = string.Join('\n', featureBase64.Where(s => !string.IsNullOrWhiteSpace(s)));
            return CallInt(
                "syncDatabase",
                "(Ljava/lang/String;F)I",
                new JValue(JNIEnv.NewString(packed)),
                new JValue(threshold));
        }));

    public static partial Task<int> AddFrameAsync(byte[] jpeg) =>
        SdkGate.RunAsync(() => Task.Run(() =>
        {
            using var bitmap = Decode(jpeg);
            return CallInt("addFrame", "(Landroid/graphics/Bitmap;)I", new JValue(bitmap));
        }));

    public static partial Task<LiveFrameResult> ApplyLiveFrameAsync(byte[] jpeg, int rotateDegrees, int maxEdge, bool feedWorker) =>
        Task.Run(() =>
        {
            using var bitmap = Decode(jpeg);
            var raw = CallString(
                "applyLiveFrame",
                "(Landroid/graphics/Bitmap;FIZ)Ljava/lang/String;",
                new JValue(bitmap),
                new JValue((float)rotateDegrees),
                new JValue(Math.Max(1, maxEdge)),
                new JValue(feedWorker));
            return ParseLive(raw, jpeg: null);
        });

    public static partial Task<LiveFrameResult> IngestLiveCameraFrameAsync(byte[] jpeg, bool frontCamera) =>
        Task.Run(() =>
        {
            using var bitmap = Decode(jpeg);
            var plan = LiveFramePrep.PlanFor(frontCamera, bitmap.Width, bitmap.Height);
            var raw = CallString(
                "applyLiveFrame",
                "(Landroid/graphics/Bitmap;FIZ)Ljava/lang/String;",
                new JValue(bitmap),
                new JValue((float)plan.RotateDegrees),
                new JValue(plan.MaxEdge),
                new JValue(true));
            return ParseLive(raw, jpeg: null);
        });

    public static partial Task<LiveFrameResult> ExportLastLiveFrameAsync() =>
        Task.Run(() =>
        {
            var b64 = CallString("exportLastLiveFrameBase64", "()Ljava/lang/String;");
            if (string.IsNullOrEmpty(b64))
            {
                return new LiveFrameResult(false, 0, 0, null);
            }

            var bytes = Convert.FromBase64String(b64);
            var size = CallString("lastLiveSize", "()Ljava/lang/String;");
            var parts = size.Split('|');
            var w = parts.Length > 0 && int.TryParse(parts[0], out var pw) ? pw : 0;
            var h = parts.Length > 1 && int.TryParse(parts[1], out var ph) ? ph : 0;
            if (w <= 0 || h <= 0)
            {
                (w, h) = CaptureLogic.JpegSize(bytes);
            }

            return new LiveFrameResult(true, w, h, bytes);
        });

    /// <summary>Lock-free vs SdkGate — FaceBridge queue is synchronized; must not wait on Detect.</summary>
    public static partial Task<string> PollEventAsync() =>
        Task.FromResult(CallString("pollEvent", "()Ljava/lang/String;"));

    static LiveFrameResult ParseLive(string raw, byte[]? jpeg)
    {
        var parts = (raw ?? "").Split('|');
        if (parts.Length < 3 || parts[0] != "1")
        {
            return new LiveFrameResult(false, 0, 0, jpeg);
        }

        _ = int.TryParse(parts[1], out var w);
        _ = int.TryParse(parts[2], out var h);
        return new LiveFrameResult(true, w, h, jpeg);
    }

    static JValue Ctx()
    {
        var context = Platform.CurrentActivity ?? global::Android.App.Application.Context
            ?? throw new InvalidOperationException("Android context is not ready.");
        return new JValue(context);
    }

    static IntPtr Class()
    {
        try
        {
            var cls = JNIEnv.FindClass(Bridge);
            if (cls == IntPtr.Zero)
            {
                throw Missing();
            }

            return cls;
        }
        catch (Java.Lang.Throwable)
        {
            JNIEnv.ExceptionClear();
            throw Missing();
        }
    }

    static InvalidOperationException Missing() =>
        new("facerecognitionsdk.aar is not in the app. Copy it to Platforms/Android/libs/.");

    static string CallString(string name, string sig, params JValue[] args)
    {
        try
        {
            var cls = Class();
            var method = JNIEnv.GetStaticMethodID(cls, name, sig);
            var result = JNIEnv.CallStaticObjectMethod(cls, method, args);
            return result == IntPtr.Zero ? "" : JNIEnv.GetString(result, JniHandleOwnership.TransferLocalRef) ?? "";
        }
        catch (Java.Lang.Throwable ex)
        {
            throw new InvalidOperationException(ex.Message ?? "Face Recognition SDK call failed.", ex);
        }
    }

    static int CallInt(string name, string sig, params JValue[] args)
    {
        try
        {
            var cls = Class();
            var method = JNIEnv.GetStaticMethodID(cls, name, sig);
            return JNIEnv.CallStaticIntMethod(cls, method, args);
        }
        catch (Java.Lang.Throwable ex)
        {
            throw new InvalidOperationException(ex.Message ?? "Face Recognition SDK call failed.", ex);
        }
    }

    static void CallVoid(string name, string sig, params JValue[] args)
    {
        try
        {
            var cls = Class();
            var method = JNIEnv.GetStaticMethodID(cls, name, sig);
            JNIEnv.CallStaticVoidMethod(cls, method, args);
        }
        catch (Java.Lang.Throwable ex)
        {
            throw new InvalidOperationException(ex.Message ?? "Face Recognition SDK call failed.", ex);
        }
    }

    static Bitmap Decode(byte[] jpeg)
    {
        var bitmap = BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length)
            ?? throw new InvalidOperationException("Could not decode the image.");
        try
        {
            using var stream = new MemoryStream(jpeg);
            using var exif = new ExifInterface(stream);
            var degrees = exif.GetAttributeInt(ExifInterface.TagOrientation, (int)Orientation.Normal) switch
            {
                (int)Orientation.Rotate90 => 90,
                (int)Orientation.Rotate180 => 180,
                (int)Orientation.Rotate270 => 270,
                _ => 0,
            };
            if (degrees == 0)
            {
                return bitmap;
            }

            using var matrix = new Matrix();
            matrix.PostRotate(degrees);
            var rotated = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true)
                ?? bitmap;
            if (rotated != bitmap)
            {
                bitmap.Recycle();
            }

            return rotated;
        }
        catch
        {
            return bitmap;
        }
    }
}
