#if IOS
using CoreGraphics;
using Foundation;
using UIKit;

namespace FaceRecognition.Net.Maui;

/// <summary>
/// Faithful port of Face-Recognition-LivenessDetection-iOS <c>CaptureOverlayView</c> —
/// drawn as a native UIView above Camera.MAUI (Clear blend for oval hole).
/// </summary>
static class NativeCaptureCoach
{
    const string HostKey = "maui.face.nativeCaptureCoach";

    public static UIView? HostFrom(View mauiView) =>
        mauiView.Handler?.PlatformView as UIView;

    /// <summary>
    /// Place coach above the camera preview only — never above title / hint / back.
    /// Calling BringSubviewToFront here made chrome flicker every RaiseOverlays tick.
    /// </summary>
    public static NativeCaptureCoachView Ensure(UIView host, UIView? aboveCamera = null)
    {
        NativeCaptureCoachView view;
        if (host.Layer.ValueForKey(new NSString(HostKey)) is NativeCaptureCoachView existing)
        {
            view = existing;
        }
        else
        {
            view = new NativeCaptureCoachView();
            host.Layer.SetValueForKey(view, new NSString(HostKey));
        }

        view.Frame = host.Bounds;
        view.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
        view.UserInteractionEnabled = false;
        if (view.Superview != host)
        {
            host.AddSubview(view);
        }

        // Sit directly above the camera preview — never BringSubviewToFront (covers title/back).
        if (aboveCamera != null && aboveCamera.Superview == host)
        {
            host.InsertSubviewAbove(view, aboveCamera);
        }

        view.SetNeedsDisplay();
        return view;
    }

    public static void Detach(View mauiView)
    {
        var host = HostFrom(mauiView);
        if (host?.Layer.ValueForKey(new NSString(HostKey)) is NativeCaptureCoachView view)
        {
            view.RemoveFromSuperview();
            host.Layer.SetValueForKey(NSNull.Null, new NSString(HostKey));
        }
    }
}

/// <summary>Mirrors iOS CaptureOverlayView draw + hold ring.</summary>
sealed class NativeCaptureCoachView : UIView
{
    // FPColor from Face-Recognition-LivenessDetection-iOS SceneDelegate
    static readonly UIColor Accent = UIColor.FromRGB(0xEA, 0xDD, 0xFF); // onPrimaryContainer
    static readonly UIColor HoldGreen = UIColor.FromRGB(0x2E, 0xE6, 0xA6);
    static readonly UIColor TickIdle = UIColor.White;
    static readonly UIColor ScrimStart = UIColor.FromRGB(28, 27, 31); // 0.110,0.106,0.122
    static readonly UIColor CaptureTertiary = UIColor.FromRGB(0x49, 0x25, 0x32); // onTertiary

    CaptureViewMode _mode = CaptureViewMode.NoFacePrepare;
    float _anim = 1.4f;
    float _hold;
    int _frameW = 720;
    int _frameH = 1280;
    bool _mirror = true;
    bool _showPose;
    float _yaw;
    float _pitch;
    CGGradient? _scrim;
    UIImage? _roiBitmap;

    public NativeCaptureCoachView()
    {
        BackgroundColor = UIColor.Clear;
        Opaque = false;
        UserInteractionEnabled = false;
        ContentMode = UIViewContentMode.Redraw;
        ClearsContextBeforeDrawing = true;
    }

    public override bool Opaque
    {
        get => false;
        set { }
    }

    public void Apply(
        CaptureViewMode mode,
        float anim,
        float hold,
        int frameW,
        int frameH,
        bool mirror,
        bool showPose,
        float yaw,
        float pitch)
    {
        _mode = mode;
        _anim = anim;
        _hold = hold;
        _frameW = Math.Max(1, frameW);
        _frameH = Math.Max(1, frameH);
        _mirror = mirror;
        _showPose = showPose;
        _yaw = yaw;
        _pitch = pitch;
        SetNeedsDisplay();
    }

    public void SetCapturedJpeg(byte[]? jpeg, int frameW, int frameH, bool mirror = false)
    {
        _roiBitmap?.Dispose();
        _roiBitmap = null;
        _mirror = mirror;
        if (jpeg == null || jpeg.Length == 0)
        {
            SetNeedsDisplay();
            return;
        }

        using var data = NSData.FromArray(jpeg);
        using var full = UIImage.LoadFromData(data);
        if (full == null)
        {
            SetNeedsDisplay();
            return;
        }

        // Prefer real JPEG pixel size when frame dims were wrong / swapped.
        var pw = full.Size.Width * full.CurrentScale;
        var ph = full.Size.Height * full.CurrentScale;
        if (pw >= 2 && ph >= 2)
        {
            _frameW = (int)Math.Round((double)pw);
            _frameH = (int)Math.Round((double)ph);
        }
        else
        {
            _frameW = Math.Max(1, frameW);
            _frameH = Math.Max(1, frameH);
        }

        // Bake orientation then crop roiRect1 (Android CaptureView.setCapturedBitmap).
        UIGraphics.BeginImageContextWithOptions(new CGSize(pw, ph), true, 1);
        full.Draw(new CGRect(0, 0, pw, ph));
        using var oriented = UIGraphics.GetImageFromCurrentImageContext();
        UIGraphics.EndImageContext();
        if (oriented?.CGImage == null)
        {
            SetNeedsDisplay();
            return;
        }

        var roi = RoiRect1(_frameW, _frameH).Integral();
        if (roi.Width < 2 || roi.Height < 2)
        {
            SetNeedsDisplay();
            return;
        }

        var sx = pw / _frameW;
        var sy = ph / _frameH;
        var crop = new CGRect(roi.X * sx, roi.Y * sy, roi.Width * sx, roi.Height * sy).Integral();
        using var cg = oriented.CGImage.WithImageInRect(crop);
        if (cg == null)
        {
            SetNeedsDisplay();
            return;
        }

        var size = new CGSize(crop.Width, crop.Height);
        UIGraphics.BeginImageContextWithOptions(size, false, 1);
        var ctx = UIGraphics.GetCurrentContext();
        if (ctx != null)
        {
            ctx.AddEllipseInRect(new CGRect(0, 0, size.Width, size.Height));
            ctx.Clip();
            if (mirror)
            {
                // Front preview is mirrored; engine JPEG is not — flip for status oval.
                ctx.TranslateCTM(size.Width, 0);
                ctx.ScaleCTM(-1, 1);
            }

            using var clipped = new UIImage(cg, 1, UIImageOrientation.Up);
            clipped.Draw(new CGRect(0, 0, size.Width, size.Height));
            _roiBitmap = UIGraphics.GetImageFromCurrentImageContext();
        }

        UIGraphics.EndImageContext();
        SetNeedsDisplay();
    }

    public override void Draw(CGRect rect)
    {
        var ctx = UIGraphics.GetCurrentContext();
        if (ctx == null || Bounds.Width < 2 || Bounds.Height < 2)
        {
            return;
        }

        var canvas = Bounds.Size;
        var roiView = RoiViewRect(canvas);
        EnsureScrim();

        // iOS CaptureOverlayView: scrim only in circle / transition modes (not prepare brackets).
        var drawScrim = _mode is CaptureViewMode.FaceCircle
            or CaptureViewMode.FaceCapturePrepare
            or CaptureViewMode.FaceCaptureDone
            || (_mode == CaptureViewMode.ToFaceCircle && _anim < 1f)
            || _mode == CaptureViewMode.FaceCircleToNoFace;

        if (drawScrim && _scrim != null)
        {
            ctx.SaveState();
            if (_mode == CaptureViewMode.FaceCircleToNoFace)
            {
                ctx.SetAlpha(1f - _anim);
            }

            ctx.DrawLinearGradient(
                _scrim,
                CGPoint.Empty,
                new CGPoint(canvas.Width, canvas.Height),
                CGGradientDrawingOptions.None);
            ctx.RestoreState();
        }

        switch (_mode)
        {
            case CaptureViewMode.NoFacePrepare:
            case CaptureViewMode.RepeatNoFacePrepare:
            case CaptureViewMode.ToFaceCircle:
            case CaptureViewMode.FaceCircleToNoFace:
                DrawCornerBrackets(ctx, roiView);
                if ((_mode == CaptureViewMode.ToFaceCircle && _anim < 1f)
                    || _mode == CaptureViewMode.FaceCircleToNoFace)
                {
                    DrawCircleEraser(ctx, roiView);
                }

                break;
            case CaptureViewMode.FaceCircle:
                DrawCircleCoach(ctx, roiView);
                break;
            case CaptureViewMode.FaceCapturePrepare:
                DrawCapturePrepare(ctx, roiView);
                break;
            case CaptureViewMode.FaceCaptureDone:
                DrawCaptureDone(ctx, roiView, canvas);
                break;
        }
    }

    void EnsureScrim()
    {
        if (_scrim != null)
        {
            return;
        }

        _scrim = new CGGradient(
            CGColorSpace.CreateDeviceRGB(),
            new[] { ScrimStart.CGColor, UIColor.Black.CGColor },
            new nfloat[] { 0, 1 });
    }

    static CGRect RoiRect1(int fw, int fh)
    {
        var margin = fw / 6.0;
        var side = fw - 2 * margin;
        var top = (fh - side) / 2.0;
        return new CGRect(margin, top, side, side);
    }

    CGRect RoiViewRect(CGSize canvas)
    {
        var roi = RoiRect1(_frameW, _frameH);
        var fw = (nfloat)_frameW;
        var fh = (nfloat)_frameH;
        var ratioView = canvas.Width / canvas.Height;
        var ratioFrame = fw / fh;
        nfloat x1, y1, x2, y2;
        if (ratioView < ratioFrame)
        {
            var dx = (canvas.Height * ratioFrame - canvas.Width) / 2;
            var ratio = canvas.Height / fh;
            x1 = (nfloat)(roi.Left * ratio - dx);
            y1 = (nfloat)(roi.Top * ratio);
            x2 = (nfloat)(roi.Right * ratio - dx);
            y2 = (nfloat)(roi.Bottom * ratio);
        }
        else
        {
            var dy = (canvas.Width / ratioFrame - canvas.Height) / 2;
            var ratio = canvas.Height / fh;
            x1 = (nfloat)(roi.Left * ratio);
            y1 = (nfloat)(roi.Top * ratio - dy);
            x2 = (nfloat)(roi.Right * ratio);
            y2 = (nfloat)(roi.Bottom * ratio - dy);
        }

        return new CGRect(x1, y1, x2 - x1, y2 - y1);
    }

    static void Scale(ref CGRect r, nfloat factor)
    {
        var dH = r.Width * (factor - 1);
        var dV = r.Height * (factor - 1);
        r = r.Inset(-dH / 2, -dV / 2);
    }

    void DrawCornerBrackets(CGContext ctx, CGRect roiView)
    {
        var scaleRoi = roiView;
        if (_mode is CaptureViewMode.NoFacePrepare or CaptureViewMode.RepeatNoFacePrepare
            || (_mode == CaptureViewMode.ToFaceCircle && _anim > 1f))
        {
            Scale(ref scaleRoi, _anim);
        }

        nfloat lineW = scaleRoi.Width / 5;
        nfloat lineH = scaleRoi.Height / 5;
        nfloat lineWOff = 0;
        nfloat lineHOff = 0;
        nfloat quadR = scaleRoi.Width / 12;

        if (_mode == CaptureViewMode.FaceCircle
            || (_mode == CaptureViewMode.ToFaceCircle && _anim < 1f)
            || _mode == CaptureViewMode.FaceCircleToNoFace)
        {
            lineW *= _anim;
            lineWOff = scaleRoi.Width / 2 * (1 - _anim);
            lineH *= _anim;
            lineHOff = scaleRoi.Height / 2 * (1 - _anim);
            quadR = scaleRoi.Width / 12 + (scaleRoi.Width / 2 - scaleRoi.Width / 12) * (1 - _anim) - 20;
        }

        ctx.SetStrokeColor(Accent.CGColor);
        ctx.SetLineWidth(10);
        ctx.SetLineCap(CGLineCap.Round);
        if (_mode == CaptureViewMode.NoFacePrepare || (_mode == CaptureViewMode.ToFaceCircle && _anim > 1f))
        {
            ctx.SetAlpha((nfloat)Math.Min(1, (1.4 - _anim) / 0.4));
        }
        else
        {
            ctx.SetAlpha(1);
        }

        DrawBracket(ctx, scaleRoi, lineW, lineH, lineWOff, lineHOff, quadR);
        ctx.SetAlpha(1);
    }

    static void DrawBracket(
        CGContext ctx,
        CGRect scaleRoi,
        nfloat lineW,
        nfloat lineH,
        nfloat lineWOff,
        nfloat lineHOff,
        nfloat quadR)
    {
        // Top-left — iOS CaptureOverlayView.drawBracket
        ctx.MoveTo(scaleRoi.Left, scaleRoi.Top + lineH + lineHOff);
        ctx.AddLineToPoint(scaleRoi.Left, scaleRoi.Top + quadR);
        ctx.AddArc(scaleRoi.Left + quadR, scaleRoi.Top + quadR, quadR, (nfloat)Math.PI, (nfloat)(Math.PI * 1.5), false);
        ctx.AddLineToPoint(scaleRoi.Left + lineW + lineWOff, scaleRoi.Top);
        ctx.StrokePath();

        ctx.MoveTo(scaleRoi.Right, scaleRoi.Top + lineH + lineHOff);
        ctx.AddLineToPoint(scaleRoi.Right, scaleRoi.Top + quadR);
        ctx.AddArc(scaleRoi.Right - quadR, scaleRoi.Top + quadR, quadR, 0, (nfloat)(-Math.PI / 2), true);
        ctx.AddLineToPoint(scaleRoi.Right - lineW - lineWOff, scaleRoi.Top);
        ctx.StrokePath();

        ctx.MoveTo(scaleRoi.Right, scaleRoi.Bottom - lineH - lineHOff);
        ctx.AddLineToPoint(scaleRoi.Right, scaleRoi.Bottom - quadR);
        ctx.AddArc(scaleRoi.Right - quadR, scaleRoi.Bottom - quadR, quadR, 0, (nfloat)(Math.PI / 2), false);
        ctx.AddLineToPoint(scaleRoi.Right - lineW - lineWOff, scaleRoi.Bottom);
        ctx.StrokePath();

        ctx.MoveTo(scaleRoi.Left, scaleRoi.Bottom - lineH - lineHOff);
        ctx.AddLineToPoint(scaleRoi.Left, scaleRoi.Bottom - quadR);
        ctx.AddArc(scaleRoi.Left + quadR, scaleRoi.Bottom - quadR, quadR, (nfloat)Math.PI, (nfloat)(Math.PI / 2), true);
        ctx.AddLineToPoint(scaleRoi.Left + lineW + lineWOff, scaleRoi.Bottom);
        ctx.StrokePath();
    }

    void DrawCircleEraser(CGContext ctx, CGRect roiView)
    {
        var startWidth = 0.8 * roiView.Width * 0.5 / Math.Cos(45 * Math.PI / 180);
        var cx = roiView.GetMidX();
        var cy = roiView.GetMidY();
        var half = roiView.Width / 2 * (1 - _anim) + (nfloat)startWidth * _anim;
        ctx.SetBlendMode(CGBlendMode.Clear);
        ctx.FillEllipseInRect(new CGRect(cx - half, cy - half, half * 2, half * 2));
        ctx.SetBlendMode(CGBlendMode.Normal);
    }

    void DrawCircleCoach(CGContext ctx, CGRect roiView)
    {
        ctx.SetBlendMode(CGBlendMode.Clear);
        ctx.FillEllipseInRect(roiView);
        ctx.SetBlendMode(CGBlendMode.Normal);

        var cx = roiView.GetMidX();
        var cy = roiView.GetMidY();
        var holdDegrees = _hold * 360f;
        ctx.SetLineWidth(8);
        for (var i = 0; i < 360; i += 5)
        {
            var th = i * Math.PI / 180.0;
            var a1 = roiView.Width / 2 + 10;
            var b1 = roiView.Height / 2 + 10;
            var a2 = roiView.Width / 2 + 40;
            var b2 = roiView.Height / 2 + 40;
            var tanTh = Math.Tan(th);
            var x1 = a1 * b1 / Math.Sqrt(b1 * b1 + a1 * a1 * tanTh * tanTh);
            var x2 = a2 * b2 / Math.Sqrt(b2 * b2 + a2 * a2 * tanTh * tanTh);
            var y1 = Math.Sqrt(Math.Max(0, 1 - (x1 / a1) * (x1 / a1))) * b1;
            var y2 = Math.Sqrt(Math.Max(0, 1 - (x1 / a1) * (x1 / a1))) * b2;
            var mod = i % 360;
            if (mod > 90 && mod < 270)
            {
                x1 = -x1;
                x2 = -x2;
            }

            if (mod > 180 && mod < 360)
            {
                y1 = -y1;
                y2 = -y2;
            }

            var from12 = (90 - mod + 360) % 360;
            var filled = _hold > 0 && from12 <= holdDegrees;
            ctx.SetStrokeColor((filled ? HoldGreen : TickIdle).CGColor);
            ctx.MoveTo(cx + (nfloat)x1, cy - (nfloat)y1);
            ctx.AddLineToPoint(cx + (nfloat)x2, cy - (nfloat)y2);
            ctx.StrokePath();
        }

        if (_hold > 0)
        {
            DrawHoldArc(ctx, roiView, _hold);
        }

        if (_showPose)
        {
            ctx.SetFillColor(Accent.ColorWithAlpha(0.5f).CGColor);
            var yaw = _mirror ? _yaw : -_yaw;
            var pitch = -_pitch;
            using (var path = new UIBezierPath())
            {
                path.MoveTo(new CGPoint(roiView.GetMidX(), roiView.Top));
                path.AddQuadCurveToPoint(
                    new CGPoint(roiView.GetMidX(), roiView.Bottom),
                    new CGPoint(roiView.GetMidX() - roiView.Width * (nfloat)Math.Sin(yaw * Math.PI / 180.0), roiView.GetMidY()));
                path.ClosePath();
                if (path.CGPath != null)
                {
                    ctx.AddPath(path.CGPath);
                    ctx.DrawPath(CGPathDrawingMode.Fill);
                }
            }

            using (var path = new UIBezierPath())
            {
                path.MoveTo(new CGPoint(roiView.Left, roiView.GetMidY()));
                path.AddQuadCurveToPoint(
                    new CGPoint(roiView.Right, roiView.GetMidY()),
                    new CGPoint(roiView.GetMidX(), roiView.GetMidY() + roiView.Width * (nfloat)Math.Sin(pitch * Math.PI / 180.0)));
                path.ClosePath();
                if (path.CGPath != null)
                {
                    ctx.AddPath(path.CGPath);
                    ctx.DrawPath(CGPathDrawingMode.Fill);
                }
            }
        }
    }

    static void DrawHoldArc(CGContext ctx, CGRect roiView, float progress)
    {
        var cx = roiView.GetMidX();
        var cy = roiView.GetMidY();
        var rx = roiView.Width / 2 + 8;
        var ry = roiView.Height / 2 + 8;
        ctx.SaveState();
        ctx.TranslateCTM(cx, cy);
        ctx.ScaleCTM(rx, ry);
        var lineWidth = 6 / Math.Max((double)Math.Min(rx, ry), 1);
        ctx.SetLineWidth((nfloat)lineWidth);
        ctx.SetLineCap(CGLineCap.Round);
        ctx.SetStrokeColor(HoldGreen.CGColor);
        var start = (nfloat)(-Math.PI / 2);
        var end = start + (nfloat)(Math.PI * 2 * progress);
        using var path = UIBezierPath.FromArc(CGPoint.Empty, 1, start, end, true);
        if (path.CGPath != null)
        {
            ctx.AddPath(path.CGPath);
            ctx.StrokePath();
        }

        ctx.RestoreState();
    }

    void DrawCapturePrepare(CGContext ctx, CGRect roiView)
    {
        var border = roiView;
        Scale(ref border, 1.04f);
        ctx.SetFillColor(CaptureTertiary.CGColor);
        ctx.FillEllipseInRect(border);

        var inner = roiView;
        Scale(ref inner, 1 - _anim);
        ctx.SetBlendMode(CGBlendMode.Clear);
        ctx.FillEllipseInRect(inner);
        ctx.SetBlendMode(CGBlendMode.Normal);
    }

    void DrawCaptureDone(CGContext ctx, CGRect roiView, CGSize canvas)
    {
        var border = roiView;
        Scale(ref border, 0.8f);
        var slideY = (canvas.Width / 5 - roiView.Top) * _anim;
        ctx.SaveState();
        ctx.TranslateCTM(0, slideY);
        // iOS CaptureOverlayView.drawCaptureDone — draw roiBitmap as-is (no mirror).
        _roiBitmap?.Draw(border);

        ctx.SetStrokeColor(CaptureTertiary.CGColor);
        ctx.SetLineWidth(15);
        ctx.StrokeEllipseInRect(border);
        ctx.RestoreState();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scrim?.Dispose();
            _scrim = null;
            _roiBitmap?.Dispose();
            _roiBitmap = null;
        }

        base.Dispose(disposing);
    }
}
#endif
