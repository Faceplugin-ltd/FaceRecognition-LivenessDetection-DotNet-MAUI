using System.Text.Json;

namespace FaceRecognition.Net.Maui;

public enum CaptureState
{
    NoFace,
    MultipleFaces,
    FitInCircle,
    MoveCloser,
    NoFront,
    FaceOccluded,
    EyeClosed,
    CaptureOk,
}

public readonly record struct RoiRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public double CenterX => (Left + Right) / 2;
    public double CenterY => (Top + Bottom) / 2;
}

public static class CaptureLogic
{
    public static string WarningFor(CaptureState state) => state switch
    {
        CaptureState.MultipleFaces => "Multiple face detected!",
        CaptureState.FitInCircle => "Fit in circle!",
        CaptureState.MoveCloser => "Move closer!",
        CaptureState.NoFront => "Not fronted face!",
        CaptureState.FaceOccluded => "Face occluded!",
        CaptureState.EyeClosed => "Eye closed!",
        _ => "",
    };

    public static bool HasLiveness(FaceBox box) =>
        !string.IsNullOrWhiteSpace(box.LivenessLabel) || (box.Liveness ?? 0) > 0.001;

    public static (double X, double Y) MapFramePoint(
        double x,
        double y,
        double fw,
        double fh,
        double vw,
        double vh,
        bool mirror)
    {
        var scale = Math.Max(vw / fw, vh / fh);
        var dx = (vw - fw * scale) / 2;
        var dy = (vh - fh * scale) / 2;
        var vx = x * scale + dx;
        var vy = y * scale + dy;
        if (mirror)
        {
            vx = vw - vx;
        }

        return (vx, vy);
    }

    public static IReadOnlyList<(double X, double Y)> MapLandmarksToCrop(
        FaceBox faceBox,
        double srcW,
        double srcH,
        double outW,
        double outH)
    {
        var lm = faceBox.Landmarks;
        var n = Math.Max(0, lm.Count / 2);
        if (n == 0 || srcW <= 0 || srcH <= 0 || outW <= 0 || outH <= 0)
        {
            return Array.Empty<(double, double)>();
        }

        var centerX = (faceBox.X1 + faceBox.X2) / 2;
        var centerY = (faceBox.Y1 + faceBox.Y2) / 2;
        var cropWidth = Math.Round((faceBox.X2 - faceBox.X1) * 1.4);
        if (cropWidth < 2)
        {
            cropWidth = Math.Max(2, Math.Max(faceBox.X2 - faceBox.X1, faceBox.Y2 - faceBox.Y1));
        }

        var cropX1 = Math.Max(0, centerX - cropWidth / 2);
        var cropY1 = Math.Max(0, centerY - cropWidth / 2);
        var cropX2 = Math.Min(srcW - 1, centerX + cropWidth / 2);
        var cropY2 = Math.Min(srcH - 1, centerY + cropWidth / 2);
        var cropSrcW = cropX2 - cropX1 + 1;
        var cropSrcH = cropY2 - cropY1 + 1;
        if (cropSrcW < 1 || cropSrcH < 1)
        {
            return Array.Empty<(double, double)>();
        }

        var sx = outW / cropSrcW;
        var sy = outH / cropSrcH;
        var mapped = new List<(double, double)>(n);
        for (var i = 0; i < n; i++)
        {
            mapped.Add(((lm[i * 2] - cropX1) * sx, (lm[i * 2 + 1] - cropY1) * sy));
        }

        return mapped;
    }

    public static List<FaceBox> MergeLiveness(IReadOnlyList<FaceBox> track, IReadOnlyList<FaceBox> pb)
    {
        if (pb.Count == 0)
        {
            return track.ToList();
        }

        return track.Select(dst =>
        {
            var src = BestMatch(dst, pb);
            return src == null ? dst : dst.WithLiveness(src.Liveness, src.LivenessLabel);
        }).ToList();
    }

    public static List<FaceBox> MergeEyes(IReadOnlyList<FaceBox> track, IReadOnlyList<FaceBox> pb, bool swapLeftRight)
    {
        if (pb.Count == 0)
        {
            return track.ToList();
        }

        return track.Select(dst =>
        {
            var src = BestMatch(dst, pb);
            if (src == null)
            {
                return dst;
            }

            return swapLeftRight
                ? dst.WithEyes(src.EyesRightLabel, src.EyesLeftLabel, src.RightEyeClosed, src.LeftEyeClosed)
                : dst.WithEyes(src.EyesLeftLabel, src.EyesRightLabel, src.LeftEyeClosed, src.RightEyeClosed);
        }).ToList();
    }

    static FaceBox? BestMatch(FaceBox dst, IReadOnlyList<FaceBox> pb)
    {
        if (pb.Count == 1)
        {
            return pb[0];
        }

        FaceBox? best = null;
        var bestIou = 0.1;
        foreach (var src in pb)
        {
            var v = IoU(dst, src);
            if (v > bestIou)
            {
                bestIou = v;
                best = src;
            }
        }

        return best ?? pb[0];
    }

    static double IoU(FaceBox a, FaceBox b)
    {
        var left = Math.Max(a.X1, b.X1);
        var top = Math.Max(a.Y1, b.Y1);
        var right = Math.Min(a.X2, b.X2);
        var bottom = Math.Min(a.Y2, b.Y2);
        var inter = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        var areaA = Math.Max(0, a.X2 - a.X1) * Math.Max(0, a.Y2 - a.Y1);
        var areaB = Math.Max(0, b.X2 - b.X1) * Math.Max(0, b.Y2 - b.Y1);
        var union = areaA + areaB - inter;
        return union <= 0 ? 0 : inter / union;
    }

    /// <summary>Android CaptureView.getROIRect — fit / size tests.</summary>
    public static RoiRect GetRoiRect(double frameW, double frameH)
    {
        var margin = frameW / 6;
        var rectHeight = ((frameW - 2 * margin) * 6) / 5;
        var top = (frameH - rectHeight) / 2;
        return new RoiRect(margin, top, frameW - margin, top + rectHeight);
    }

    /// <summary>Android CaptureView.getROIRect1 — circular / oval guide.</summary>
    public static RoiRect GetRoiRect1(double frameW, double frameH)
    {
        var margin = frameW / 6;
        var rectHeight = frameW - 2 * margin;
        var top = (frameH - rectHeight) / 2;
        return new RoiRect(margin, top, frameW - margin, top + rectHeight);
    }

    /// <summary>Android CaptureView.mapRoiToView — FILL_CENTER mapping.</summary>
    public static RoiRect MapRoiToView(double frameW, double frameH, double viewW, double viewH)
    {
        var roi = GetRoiRect1(frameW, frameH);
        var ratioView = viewW / viewH;
        var ratioFrame = frameW / frameH;
        var ratio = viewH / frameH;
        double dx = 0, dy = 0;
        if (ratioView < ratioFrame)
        {
            dx = (viewH * ratioFrame - viewW) / 2;
        }
        else
        {
            dy = (viewW / ratioFrame - viewH) / 2;
        }

        return new RoiRect(
            roi.Left * ratio - dx,
            roi.Top * ratio - dy,
            roi.Right * ratio - dx,
            roi.Bottom * ratio - dy);
    }

    public static (int Width, int Height) JpegSize(byte[] jpeg)
    {
        try
        {
            for (var i = 0; i < jpeg.Length - 9; i++)
            {
                if (jpeg[i] != 0xFF)
                {
                    continue;
                }

                var marker = jpeg[i + 1];
                if (marker is 0xC0 or 0xC1 or 0xC2)
                {
                    var height = (jpeg[i + 5] << 8) | jpeg[i + 6];
                    var width = (jpeg[i + 7] << 8) | jpeg[i + 8];
                    if (width > 0 && height > 0)
                    {
                        return (width, height);
                    }
                }
            }
        }
        catch
        {
        }

        return (720, 1280);
    }

    public static CaptureState CheckFace(IReadOnlyList<FaceBox> boxes, AppSettings settings, double frameW, double frameH)
    {
        if (boxes.Count == 0)
        {
            return CaptureState.NoFace;
        }

        if (boxes.Count > 1)
        {
            return CaptureState.MultipleFaces;
        }

        var face = boxes[0];
        var fw = frameW > 0 ? frameW : 720;
        var fh = frameH > 0 ? frameH : 1280;
        var roi = GetRoiRect(fw, fh);
        var roiLeft = roi.Left;
        var roiTop = roi.Top;
        var roiRight = roi.Right;
        var roiBottom = roi.Bottom;
        var roiW = roi.Width;
        var roiH = roi.Height;

        double faceLeft = double.MaxValue;
        double faceRight = 0;
        double faceBottom = 0;
        var marks = face.Landmarks;
        var nMarks = Math.Min(marks.Count / 2, marks.Count / 2);
        if (nMarks >= 5)
        {
            for (var i = 0; i < nMarks; i++)
            {
                var lx = marks[i * 2];
                var ly = marks[i * 2 + 1];
                faceLeft = Math.Min(faceLeft, lx);
                faceRight = Math.Max(faceRight, lx);
                faceBottom = Math.Max(faceBottom, ly);
            }
        }
        else
        {
            faceLeft = face.X1;
            faceRight = face.X2;
            faceBottom = face.Y2;
        }

        const double sizeRate = 0.3;
        const double interRate = 0.03;
        var centerY = (face.Y2 + face.Y1) / 2;
        var topY = centerY - (face.Height * 2) / 3;
        var interX = Math.Max(0, roiLeft - faceLeft) + Math.Max(0, faceRight - roiRight);
        var interY = Math.Max(0, roiTop - topY) + Math.Max(0, faceBottom - roiBottom);
        if (interX / roiW > interRate || interY / roiH > interRate)
        {
            return CaptureState.FitInCircle;
        }

        if (face.Width * face.Height < roiW * roiH * sizeRate)
        {
            return CaptureState.MoveCloser;
        }

        if (Math.Abs(face.Yaw ?? 0) > settings.YawThreshold
            || Math.Abs(face.Roll ?? 0) > settings.RollThreshold
            || Math.Abs(face.Pitch ?? 0) > settings.PitchThreshold)
        {
            return CaptureState.NoFront;
        }

        if ((face.MaskLabel ?? "").Contains("yes", StringComparison.OrdinalIgnoreCase))
        {
            return CaptureState.FaceOccluded;
        }

        var left = face.EyesLeftLabel ?? "";
        var right = face.EyesRightLabel ?? "";
        if (left.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || right.Contains("closed", StringComparison.OrdinalIgnoreCase))
        {
            return CaptureState.EyeClosed;
        }

        if (left.Length == 0 && right.Length == 0
            && ((face.LeftEyeClosed ?? 0) > settings.EyeCloseThreshold
                || (face.RightEyeClosed ?? 0) > settings.EyeCloseThreshold))
        {
            return CaptureState.EyeClosed;
        }

        return CaptureState.CaptureOk;
    }
}

public static class FaceJson
{
    public static List<FaceBox> ParseDetect(string json)
    {
        var boxes = new List<FaceBox>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return boxes;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            JsonElement data;
            if (root.ValueKind == JsonValueKind.Array)
            {
                data = root;
            }
            else if (root.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Array)
            {
            }
            else if (root.TryGetProperty("faces", out data) && data.ValueKind == JsonValueKind.Array)
            {
            }
            else
            {
                return boxes;
            }

            foreach (var face in data.EnumerateArray())
            {
                var box = FromFace(face);
                if (box != null)
                {
                    boxes.Add(box);
                }
            }
        }
        catch
        {
        }

        return boxes;
    }

    public static string? FeatureBase64(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in results.EnumerateArray())
                {
                    var feature = FirstFeature(item);
                    if (feature != null)
                    {
                        return feature;
                    }
                }
            }

            var direct = FirstFeature(root);
            if (direct != null)
            {
                return direct;
            }

            // RN FRSFeatureB64FromExtractJSON fallbacks.
            if (root.TryGetProperty("feature", out var feat) && feat.ValueKind == JsonValueKind.String)
            {
                var text = feat.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String)
            {
                var text = data.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            if (root.TryGetProperty("featureBase64", out var fb) && fb.ValueKind == JsonValueKind.String)
            {
                var text = fb.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch
        {
        }

        return null;
    }

    /// <summary>
    /// `label · levelName · Recognition · Liveness`, the line the Android settings screen and
    /// the Flutter and Ionic apps show. Neither native app refuses work on a thin tier, so
    /// this only reports what the license allows; it does not gate anything.
    /// </summary>
    public static string LicenseTier(string json)
    {
        var parts = new List<string> { LicenseLabel(json) };
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            var licensed = root.TryGetProperty("licensed", out var l) && l.ValueKind == JsonValueKind.True;
            if (licensed
                && root.TryGetProperty("levelName", out var level)
                && level.ValueKind == JsonValueKind.String)
            {
                var name = level.GetString() ?? "";
                if (!string.IsNullOrWhiteSpace(name)
                    && !name.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(name);
                }
            }
            if (root.TryGetProperty("recognition", out var rec) && rec.ValueKind == JsonValueKind.True)
            {
                parts.Add("Recognition");
            }
            if (root.TryGetProperty("liveness", out var live) && live.ValueKind == JsonValueKind.True)
            {
                parts.Add("Liveness");
            }
        }
        catch
        {
            // A label on its own is still worth showing.
        }

        return string.Join(" · ", parts);
    }

    public static string LicenseLabel(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String)
            {
                var text = label.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch
        {
        }

        return "Not licensed";
    }

    static string? FirstFeature(JsonElement parent)
    {
        if (!parent.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var feature in features.EnumerateArray())
        {
            if (feature.TryGetProperty("feature", out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    static FaceBox? FromFace(JsonElement face)
    {
        double x1, y1, x2, y2;
        if (face.TryGetProperty("faceRegion", out var region) && region.ValueKind == JsonValueKind.Object)
        {
            var x = Num(region, "x");
            var y = Num(region, "y");
            var w = Num(region, "width");
            var h = Num(region, "height");
            x1 = x;
            y1 = y;
            x2 = x + w;
            y2 = y + h;
        }
        else if (face.TryGetProperty("x1", out _))
        {
            x1 = Num(face, "x1");
            y1 = Num(face, "y1");
            x2 = Num(face, "x2");
            y2 = Num(face, "y2");
        }
        else
        {
            return null;
        }

        double? yaw = OptNum(face, "yaw");
        double? pitch = OptNum(face, "pitch");
        double? roll = OptNum(face, "roll");
        if (face.TryGetProperty("facePose", out var pose) && pose.ValueKind == JsonValueKind.Object)
        {
            yaw ??= Num(pose, "yaw");
            pitch ??= Num(pose, "pitch");
            roll ??= Num(pose, "roll");
        }

        var landmarks = new List<double>();
        if (face.TryGetProperty("landmarks", out var lm) && lm.ValueKind == JsonValueKind.Array)
        {
            foreach (var n in lm.EnumerateArray())
            {
                if (n.TryGetDouble(out var v))
                {
                    landmarks.Add(v);
                }
            }
        }
        else if (face.TryGetProperty("facePoints", out var points) && points.ValueKind == JsonValueKind.Array)
        {
            foreach (var point in points.EnumerateArray())
            {
                landmarks.Add(Num(point, "x"));
                landmarks.Add(Num(point, "y"));
            }
        }

        var attrs = new Dictionary<string, string>();
        string? livenessLabel = Str(face, "livenessLabel");
        string? gender = Str(face, "genderLabel");
        string? emotion = Str(face, "emotionLabel");
        string? mask = Str(face, "maskLabel");
        string? eyesLeft = Str(face, "eyesLeftLabel");
        string? eyesRight = Str(face, "eyesRightLabel");
        string? glasses = Str(face, "glassesLabel");
        string? sunglasses = Str(face, "sunglassesLabel");
        string? occlusion = Str(face, "occlusionLabel");
        string? qualityLabel = Str(face, "qualityLabel");
        double? liveness = OptNum(face, "liveness");
        double? age = OptNum(face, "age");
        double? leftClosed = OptNum(face, "left_eye_closed");
        double? rightClosed = OptNum(face, "right_eye_closed");
        double? faceQuality = OptNum(face, "face_quality");
        double? faceLuminance = OptNum(face, "face_luminance");
        if (face.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in attributes.EnumerateObject())
            {
                var text = AttrText(prop.Value);
                if (text.Length > 0)
                {
                    attrs[prop.Name] = text;
                }

                var key = prop.Name.ToLowerInvariant();
                if (key.Contains("liveness"))
                {
                    livenessLabel ??= text;
                    liveness ??= AttrNumber(prop.Value);
                }
                else if (key.Contains("gender"))
                {
                    gender ??= text;
                }
                else if (key.Contains("emotion"))
                {
                    emotion ??= text;
                }
                else if (key.Contains("sunglass"))
                {
                    sunglasses ??= text;
                }
                else if (key.Contains("glass"))
                {
                    glasses ??= text;
                }
                else if (key.Contains("mask"))
                {
                    mask ??= text;
                }
                else if (key.Contains("occlu"))
                {
                    occlusion ??= text;
                }
                else if (key.Contains("age"))
                {
                    age ??= AttrNumber(prop.Value);
                }
                else if (key.Contains("quality"))
                {
                    qualityLabel ??= text;
                    faceQuality ??= AttrNumber(prop.Value);
                }
                else if (key.Contains("luminance"))
                {
                    faceLuminance ??= AttrNumber(prop.Value);
                }
                else if (key.Contains("left") && key.Contains("eye"))
                {
                    eyesLeft ??= text;
                    leftClosed ??= AttrNumber(prop.Value);
                }
                else if (key.Contains("right") && key.Contains("eye"))
                {
                    eyesRight ??= text;
                    rightClosed ??= AttrNumber(prop.Value);
                }
            }
        }

        return new FaceBox
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Yaw = yaw,
            Pitch = pitch,
            Roll = roll,
            Liveness = liveness,
            LivenessLabel = livenessLabel,
            GenderLabel = gender,
            EmotionLabel = emotion,
            MaskLabel = mask,
            EyesLeftLabel = eyesLeft,
            EyesRightLabel = eyesRight,
            GlassesLabel = glasses,
            SunglassesLabel = sunglasses,
            OcclusionLabel = occlusion,
            LeftEyeClosed = leftClosed,
            RightEyeClosed = rightClosed,
            Age = age,
            FaceQuality = faceQuality,
            FaceLuminance = faceLuminance,
            QualityLabel = qualityLabel,
            Template = Str(face, "template"),
            TemplateMs = Num(face, "templateMs"),
            Landmarks = landmarks,
            Attributes = attrs,
        };
    }

    static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static double? OptNum(JsonElement el, string name) =>
        el.TryGetProperty(name, out var value) && value.TryGetDouble(out var n) ? n : null;

    static double Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var value) && value.TryGetDouble(out var n) ? n : 0;

    static string AttrText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var inner))
        {
            return inner.ToString();
        }

        return value.ValueKind is JsonValueKind.Number ? value.ToString() : "";
    }

    static double? AttrNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n))
        {
            return n;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("confidence", out var conf) && conf.TryGetDouble(out var c))
            {
                return c;
            }

            if (value.TryGetProperty("value", out var inner) && inner.TryGetDouble(out var v))
            {
                return v;
            }
        }

        return null;
    }
}

public readonly record struct WorkerFace(
    double X,
    double Y,
    double Width,
    double Height,
    int? PersonIndex,
    double? Score,
    bool Matched,
    bool Weak,
    double Yaw,
    double Pitch,
    double Roll,
    IReadOnlyList<double> Landmarks,
    double? Liveness,
    string? LivenessLabel);

public readonly record struct WorkerTracking(
    double FrameWidth,
    double FrameHeight,
    IReadOnlyList<WorkerFace> Faces);

public static class WorkerJson
{
    public static WorkerTracking? ParseTracking(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var ev) || ev.GetString() != "tracking")
            {
                return null;
            }

            var faces = new List<WorkerFace>();
            if (root.TryGetProperty("faces", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var face in arr.EnumerateArray())
                {
                    var parsed = ParseFace(face);
                    if (parsed != null)
                    {
                        faces.Add(parsed.Value);
                    }
                }
            }

            return new WorkerTracking(
                Num(root, "frame_width"),
                Num(root, "frame_height"),
                faces);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<WorkerFace> TrackingFaces(string json) =>
        ParseTracking(json)?.Faces ?? Array.Empty<WorkerFace>();

    /// <summary>Standalone match event (Flutter VideoWorkerMatchEvent).</summary>
    public static (bool Matched, int? PersonIndex, double? Score)? ParseMatch(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("event", out var ev) || ev.GetString() != "match")
            {
                return null;
            }

            var matched = root.TryGetProperty("matched", out var m) && m.ValueKind == JsonValueKind.True;
            int? person = null;
            double? score = null;
            if (root.TryGetProperty("person_index", out var idx) && idx.TryGetInt32(out var i))
            {
                person = i;
            }

            if (root.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var s))
            {
                score = s;
            }

            return (matched, person, score);
        }
        catch
        {
            return null;
        }
    }

    public static FaceBox ToFaceBox(WorkerFace face) =>
        new()
        {
            X1 = Math.Round(face.X),
            Y1 = Math.Round(face.Y),
            X2 = Math.Round(face.X + face.Width),
            Y2 = Math.Round(face.Y + face.Height),
            Yaw = face.Yaw,
            Pitch = face.Pitch,
            Roll = face.Roll,
            Liveness = face.Liveness,
            LivenessLabel = face.LivenessLabel,
            Landmarks = face.Landmarks,
        };

    static WorkerFace? ParseFace(JsonElement face)
    {
        if (!face.TryGetProperty("faceRegion", out var region))
        {
            return null;
        }

        int? person = null;
        double? score = null;
        var matched = false;
        if (face.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.Object)
        {
            matched = match.TryGetProperty("matched", out var m) && m.ValueKind == JsonValueKind.True;
            if (match.TryGetProperty("person_index", out var idx) && idx.TryGetInt32(out var i))
            {
                person = i;
            }

            if (match.TryGetProperty("score", out var sc) && sc.TryGetDouble(out var s))
            {
                score = s;
            }
        }

        double yaw = 0, pitch = 0, roll = 0;
        if (face.TryGetProperty("facePose", out var pose) && pose.ValueKind == JsonValueKind.Object)
        {
            yaw = Num(pose, "yaw");
            pitch = Num(pose, "pitch");
            roll = Num(pose, "roll");
        }

        var landmarks = new List<double>();
        if (face.TryGetProperty("facePoints", out var points) && points.ValueKind == JsonValueKind.Array)
        {
            foreach (var point in points.EnumerateArray())
            {
                var x = Num(point, "x");
                var y = Num(point, "y");
                if (x == 0 && y == 0)
                {
                    continue;
                }

                landmarks.Add(x);
                landmarks.Add(y);
            }
        }

        double? liveness = null;
        string? livenessLabel = null;
        if (face.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in attrs.EnumerateObject())
            {
                if (!prop.Name.Contains("liveness", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                livenessLabel = AttrText(prop.Value);
                liveness = AttrNumber(prop.Value);
            }
        }

        var weak = face.TryGetProperty("weak", out var weakEl) && weakEl.ValueKind == JsonValueKind.True;
        return new WorkerFace(
            Num(region, "x"),
            Num(region, "y"),
            Num(region, "width"),
            Num(region, "height"),
            person,
            score,
            matched,
            weak,
            yaw,
            pitch,
            roll,
            landmarks,
            liveness,
            livenessLabel);
    }

    static double Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var value) && value.TryGetDouble(out var n) ? n : 0;

    static string AttrText(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var inner))
        {
            return inner.ToString();
        }

        return value.ValueKind is JsonValueKind.Number ? value.ToString() : "";
    }

    static double? AttrNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n))
        {
            return n;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("confidence", out var conf) && conf.TryGetDouble(out var c))
            {
                return c;
            }

            if (value.TryGetProperty("value", out var inner) && inner.TryGetDouble(out var v))
            {
                return v;
            }
        }

        return null;
    }
}
