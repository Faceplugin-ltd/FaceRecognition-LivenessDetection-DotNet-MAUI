namespace FaceRecognition.Net.Maui;

/// <summary>RN FaceOverlay — cyan track / green REAL / red SPOOF + landmarks.</summary>
public class IdentifyOverlayDrawable : IDrawable
{
    public IReadOnlyList<FaceBox> Boxes { get; set; } = Array.Empty<FaceBox>();
    public double FrameWidth { get; set; } = 480;
    public double FrameHeight { get; set; } = 640;
    public bool Mirror { get; set; }
    public double LivenessThreshold { get; set; } = 0.8;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var fw = FrameWidth > 0 ? FrameWidth : 480;
        var fh = FrameHeight > 0 ? FrameHeight : 640;
        var settings = new AppSettings { LivenessThreshold = LivenessThreshold };
        foreach (var box in Boxes)
        {
            var p1 = CaptureLogic.MapFramePoint(box.X1, box.Y1, fw, fh, dirtyRect.Width, dirtyRect.Height, Mirror);
            var p2 = CaptureLogic.MapFramePoint(box.X2, box.Y2, fw, fh, dirtyRect.Width, dirtyRect.Height, Mirror);
            var left = (float)Math.Min(p1.X, p2.X);
            var right = (float)Math.Max(p1.X, p2.X);
            var top = (float)Math.Min(p1.Y, p2.Y);
            var bottom = (float)Math.Max(p1.Y, p2.Y);
            var known = CaptureLogic.HasLiveness(box);
            var live = known && ResultDetails.LivenessPassed(settings, box.Liveness ?? 0, box.LivenessLabel);
            var color = !known
                ? Color.FromArgb("#00FFFF")
                : live ? Color.FromArgb("#00FF00") : Color.FromArgb("#FF0000");
            canvas.StrokeColor = color;
            canvas.StrokeSize = 5;
            canvas.DrawRectangle(left, top, right - left, bottom - top);

            var lm = box.Landmarks;
            var n = lm.Count / 2;
            canvas.FillColor = color;
            for (var i = 0; i < n; i++)
            {
                var pt = CaptureLogic.MapFramePoint(
                    lm[i * 2],
                    lm[i * 2 + 1],
                    fw,
                    fh,
                    dirtyRect.Width,
                    dirtyRect.Height,
                    Mirror);
                canvas.FillCircle((float)pt.X, (float)pt.Y, 5);
            }

            if (known)
            {
                canvas.FontColor = color;
                canvas.FontSize = 16;
                // iOS FaceIdentifyOverlayView — REAL/SPOOF with score.
                var score = box.Liveness ?? 0;
                canvas.DrawString(
                    live ? $"REAL {score:0.##}" : $"SPOOF {score:0.##}",
                    left + 10,
                    top - 28,
                    HorizontalAlignment.Left);
            }
        }
    }
}
