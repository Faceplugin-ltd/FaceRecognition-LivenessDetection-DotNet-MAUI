namespace FaceRecognition.Net.Maui;

public enum CaptureViewMode
{
    NoFacePrepare,
    RepeatNoFacePrepare,
    ToFaceCircle,
    FaceCircleToNoFace,
    FaceCircle,
    FaceCapturePrepare,
    FaceCaptureDone,
}

/// <summary>
/// Native <c>CaptureView</c> / iOS <c>CaptureOverlayView</c> coach — corner brackets,
/// oval scrim hole, hold tick ring, pose guides, capture-done oval.
/// </summary>
public class CaptureOverlayDrawable : IDrawable
{
    // Android CaptureView / Flutter CaptureOverlay.
    static readonly Color Bracket = Color.FromArgb("#EADDFF");
    static readonly Color TickIdle = Colors.White;
    static readonly Color HoldGreen = Color.FromArgb("#2EE6A6");
    static readonly Color OnTertiary = Color.FromArgb("#492532");
    static readonly Color PoseFill = Color.FromRgba(208, 188, 255, 128);
    static readonly Color ScrimTop = Color.FromArgb("#1C1B1F");

    public CaptureViewMode ViewMode { get; set; } = CaptureViewMode.NoFacePrepare;
    public double Anim { get; set; } = 1.4;
    public double HoldProgress { get; set; }
    public double? Yaw { get; set; }
    public double? Pitch { get; set; }
    public bool Mirror { get; set; }
    public bool ShowPoseGuides { get; set; }
    public double FrameWidth { get; set; } = 720;
    public double FrameHeight { get; set; } = 1280;
    public Microsoft.Maui.Graphics.IImage? CapturedImage { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width < 2 || dirtyRect.Height < 2)
        {
            return;
        }

        var fw = FrameWidth > 0 ? FrameWidth : 720;
        var fh = FrameHeight > 0 ? FrameHeight : 1280;
        var roi = CaptureLogic.MapRoiToView(fw, fh, dirtyRect.Width, dirtyRect.Height);
        var cx = (float)roi.CenterX;
        var cy = (float)roi.CenterY;
        var baseW = (float)Math.Max(1, roi.Width);
        var baseH = (float)Math.Max(1, roi.Height);
        var scale = Anim;
        var mode = ViewMode;

        var showCorners = mode is CaptureViewMode.NoFacePrepare
            or CaptureViewMode.RepeatNoFacePrepare
            or CaptureViewMode.ToFaceCircle
            or CaptureViewMode.FaceCircleToNoFace;
        var showCircle = mode is CaptureViewMode.FaceCircle
            or CaptureViewMode.FaceCapturePrepare
            or CaptureViewMode.FaceCaptureDone
            || (mode == CaptureViewMode.ToFaceCircle && scale < 1)
            || mode == CaptureViewMode.FaceCircleToNoFace;

        var cornerScale = mode is CaptureViewMode.NoFacePrepare
            or CaptureViewMode.RepeatNoFacePrepare
            || (mode == CaptureViewMode.ToFaceCircle && scale > 1)
            ? scale
            : 1.0;
        var rw = (float)(baseW * cornerScale);
        var rh = (float)(baseH * cornerScale);
        var left = cx - rw / 2;
        var top = cy - rh / 2;
        var right = cx + rw / 2;
        var bottom = cy + rh / 2;

        var scrimAlpha = mode == CaptureViewMode.FaceCircleToNoFace
            ? (float)(1 - Math.Clamp(scale, 0, 1))
            : showCircle ? 1f : 0f;

        var holeR = HoleRadius(mode, scale, baseW);
        if (showCircle && scrimAlpha > 0)
        {
            DrawScrimWithHole(canvas, dirtyRect, cx, cy, holeR, scrimAlpha, mode == CaptureViewMode.FaceCaptureDone);
        }

        if (mode == CaptureViewMode.FaceCaptureDone)
        {
            var doneScale = 0.8f;
            var dw = baseW * doneScale;
            var dh = baseH * doneScale;
            var lift = (float)((dirtyRect.Width / 5 - roi.Top) * scale);
            var dest = new RectF(cx - dw / 2, cy - dh / 2 + lift, dw, dh);
            // CapturedImage is already roiRect1-cropped (+ front mirror) via CaptureRoiPreview.
            if (CapturedImage != null)
            {
                canvas.SaveState();
                var clip = new PathF();
                clip.AppendEllipse(dest);
                canvas.ClipPath(clip);
                canvas.DrawImage(CapturedImage, dest.X, dest.Y, dest.Width, dest.Height);
                canvas.RestoreState();
            }

            canvas.StrokeColor = OnTertiary;
            canvas.StrokeSize = 15;
            canvas.DrawEllipse(dest);
            return;
        }

        if (showCorners)
        {
            var t = mode is CaptureViewMode.FaceCircleToNoFace
                || (mode == CaptureViewMode.ToFaceCircle && scale < 1)
                ? Math.Clamp(scale, 0, 1)
                : 1;
            var alpha = mode is CaptureViewMode.NoFacePrepare
                || (mode == CaptureViewMode.ToFaceCircle && scale > 1)
                ? Math.Clamp((1.4 - scale) / 0.4, 0, 1)
                : 1;
            canvas.StrokeColor = Bracket.WithAlpha((float)alpha);
            canvas.StrokeSize = 10;
            canvas.StrokeLineCap = LineCap.Round;
            var lw = (float)(rw / 5 * t);
            var lh = (float)(rh / 5 * t);
            var ox = (float)((rw / 2) * (1 - t));
            var oy = (float)((rh / 2) * (1 - t));
            var quadR = (float)Math.Max(8, rw / 12);
            if (mode is CaptureViewMode.FaceCircleToNoFace
                || (mode == CaptureViewMode.ToFaceCircle && scale < 1))
            {
                quadR = (float)Math.Max(8, rw / 12 + (rw / 2 - rw / 12) * (1 - t) - 20);
            }

            DrawCorner(canvas, left + ox, top + oy, lw, lh, quadR, true, true);
            DrawCorner(canvas, right - ox, top + oy, lw, lh, quadR, false, true);
            DrawCorner(canvas, right - ox, bottom - oy, lw, lh, quadR, false, false);
            DrawCorner(canvas, left + ox, bottom - oy, lw, lh, quadR, true, false);
        }

        if (mode == CaptureViewMode.FaceCapturePrepare)
        {
            var outer = baseW * 1.04f;
            var inner = holeR * 2;
            var ring = new PathF();
            ring.AppendEllipse(cx - outer / 2, cy - outer / 2, outer, outer);
            if (inner > 2)
            {
                ring.AppendEllipse(cx - inner / 2, cy - inner / 2, inner, inner);
            }

            canvas.FillColor = OnTertiary;
            canvas.FillPath(ring, WindingMode.EvenOdd);
            return;
        }

        if (mode != CaptureViewMode.FaceCircle)
        {
            return;
        }

        left = cx - baseW / 2;
        top = cy - baseH / 2;
        var a1 = baseW / 2 + 10;
        var b1 = baseH / 2 + 10;
        var a2 = baseW / 2 + 40;
        var b2 = baseH / 2 + 40;
        canvas.StrokeSize = 8;
        canvas.StrokeLineCap = LineCap.Round;
        for (var i = 0; i < 360; i += 5)
        {
            var th = i * Math.PI / 180;
            var tan = Math.Tan(th);
            var den1 = Math.Sqrt(b1 * b1 + a1 * a1 * tan * tan);
            var den2 = Math.Sqrt(b2 * b2 + a2 * a2 * tan * tan);
            if (!double.IsFinite(den1) || !double.IsFinite(den2) || den1 == 0 || den2 == 0)
            {
                continue;
            }

            var x1 = (a1 * b1) / den1;
            var x2 = (a2 * b2) / den2;
            var ratio = 1 - (x1 / a1) * (x1 / a1);
            if (ratio < 0 || !double.IsFinite(ratio))
            {
                continue;
            }

            var y1 = Math.Sqrt(ratio) * b1;
            var y2 = Math.Sqrt(ratio) * b2;
            var mod = i % 360;
            if (mod is > 90 and < 270)
            {
                x1 = -x1;
                x2 = -x2;
            }

            if (mod is > 180 and < 360)
            {
                y1 = -y1;
                y2 = -y2;
            }

            if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2))
            {
                continue;
            }

            var from12 = (90 - mod + 360) % 360;
            var filled = HoldProgress > 0 && from12 <= HoldProgress * 360;
            canvas.StrokeColor = filled ? HoldGreen : TickIdle;
            canvas.DrawLine(
                cx + (float)x1,
                cy - (float)y1,
                cx + (float)x2,
                cy - (float)y2);
        }

        if (HoldProgress > 0)
        {
            canvas.StrokeColor = HoldGreen;
            canvas.StrokeSize = 8;
            canvas.StrokeLineCap = LineCap.Round;
            var sweep = (float)(Math.Clamp(HoldProgress, 0, 1) * 360);
            var pad = 14f;
            canvas.DrawArc(left - pad, top - pad, baseW + pad * 2, baseH + pad * 2, -90, sweep, false, false);
        }

        if (ShowPoseGuides && (Yaw != null || Pitch != null))
        {
            var yaw = Mirror ? (Yaw ?? 0) : -(Yaw ?? 0);
            var pitch = -(Pitch ?? 0);
            canvas.FillColor = PoseFill;
            var yawOffset = (float)(baseW * Math.Sin(yaw * Math.PI / 180));
            canvas.FillPath(BuildYawPath(cx, top, cy, top + baseH, yawOffset));
            var pitchOffset = (float)(baseW * Math.Sin(pitch * Math.PI / 180));
            canvas.FillPath(BuildPitchPath(left, cy, left + baseW, pitchOffset));
        }
    }

    static void DrawScrimWithHole(
        ICanvas canvas,
        RectF dirty,
        float cx,
        float cy,
        float holeR,
        float alpha,
        bool solid)
    {
        var path = new PathF();
        path.MoveTo(dirty.Left, dirty.Top);
        path.LineTo(dirty.Right, dirty.Top);
        path.LineTo(dirty.Right, dirty.Bottom);
        path.LineTo(dirty.Left, dirty.Bottom);
        path.Close();
        if (!solid && holeR > 1)
        {
            var d = holeR * 2;
            path.AppendEllipse(cx - holeR, cy - holeR, d, d);
        }

        var a = (float)Math.Clamp(alpha, 0, 1);
        canvas.FillColor = ScrimTop.WithAlpha(a);
        canvas.FillPath(path, WindingMode.EvenOdd);
    }

    static float HoleRadius(CaptureViewMode mode, double scale, float baseW)
    {
        if (mode == CaptureViewMode.FaceCapturePrepare)
        {
            return (baseW / 2) * (float)Math.Max(0.01, 1 - Math.Clamp(scale, 0, 1));
        }

        if (mode is CaptureViewMode.ToFaceCircle or CaptureViewMode.FaceCircleToNoFace)
        {
            var start = (0.8 * baseW * 0.5) / Math.Cos(45 * Math.PI / 180);
            return (float)((baseW / 2) * (1 - scale) + start * scale);
        }

        return baseW / 2;
    }

    static void DrawCorner(ICanvas canvas, float x, float y, float w, float h, float radius, bool left, bool top)
    {
        var dx = left ? 1f : -1f;
        var dy = top ? 1f : -1f;
        var r = Math.Min(radius, Math.Min(w, h));
        var path = new PathF();
        path.MoveTo(x + dx * w, y);
        path.LineTo(x + dx * r, y);
        path.QuadTo(x, y, x, y + dy * r);
        path.LineTo(x, y + dy * h);
        canvas.DrawPath(path);
    }

    static PathF BuildYawPath(float cx, float top, float cy, float bottom, float offset)
    {
        var path = new PathF();
        path.MoveTo(cx, top);
        path.QuadTo(cx - offset, cy, cx, bottom);
        path.QuadTo(cx - offset / 3, cy, cx, top);
        path.Close();
        return path;
    }

    static PathF BuildPitchPath(float left, float cy, float right, float offset)
    {
        var path = new PathF();
        path.MoveTo(left, cy);
        path.QuadTo((left + right) / 2, cy + offset, right, cy);
        path.QuadTo((left + right) / 2, cy + offset / 3, left, cy);
        path.Close();
        return path;
    }
}
