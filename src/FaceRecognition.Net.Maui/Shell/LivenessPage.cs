using Camera.MAUI;

namespace FaceRecognition.Net.Maui;

/// <summary>
/// Face boxes with their liveness score, drawn over the preview.
///
/// The engine frame is unmirrored. Camera.MAUI on iOS already shows that same X, so the box
/// is flipped only on Android's front preview. The score is a label, not canvas text: on iOS
/// the canvas paints that string in the fill colour, which is the chip, so it vanishes.
/// </summary>
public class LivenessOverlayDrawable : IDrawable
{
    static readonly Color Live = Color.FromArgb("#2EE6A6");
    static readonly Color Fake = Color.FromArgb("#FF6B6B");

    public IReadOnlyList<FaceBox> Faces { get; set; } = Array.Empty<FaceBox>();
    public double FrameWidth { get; set; }
    public double FrameHeight { get; set; }
    public bool Mirrored { get; set; } = true;
    public double Threshold { get; set; } = 0.5;

    public void Draw(ICanvas canvas, RectF dirty)
    {
        if (FrameWidth <= 0 || FrameHeight <= 0 || Faces.Count == 0)
        {
            return;
        }

        var scale = Math.Max(dirty.Width / FrameWidth, dirty.Height / FrameHeight);
        var dx = (dirty.Width - FrameWidth * scale) / 2;
        var dy = (dirty.Height - FrameHeight * scale) / 2;

        foreach (var face in Faces)
        {
            var live = (face.Liveness ?? 0) >= Threshold;
            var colour = live ? Live : Fake;

            var left = (float)(face.X1 * scale + dx);
            var right = (float)(face.X2 * scale + dx);
            var top = (float)(face.Y1 * scale + dy);
            var bottom = (float)(face.Y2 * scale + dy);
            if (Mirrored)
            {
                var mirroredLeft = dirty.Width - right;
                right = dirty.Width - left;
                left = mirroredLeft;
            }

            canvas.StrokeColor = colour;
            canvas.StrokeSize = 3;
            canvas.DrawRoundedRectangle(left, top, right - left, bottom - top, 9);
        }
    }
}

/// <summary>
/// Liveness on its own: the camera, a box per face and that face's liveness score. There is
/// nothing to press — no shutter, no gallery, no result screen. It is the plainest possible
/// demonstration that the engine tells a live face from a photo of one.
/// </summary>
public class LivenessPage : ContentPage
{
    readonly CameraView _camera = new()
    {
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill,
    };

    readonly LivenessOverlayDrawable _drawable = new();
    readonly GraphicsView _overlay;
    readonly Label _score = new()
    {
        TextColor = Colors.White,
        FontSize = 17,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center,
    };

    readonly Border _scoreHost;
    readonly Label _hint = new()
    {
        TextColor = Colors.White,
        FontSize = 14,
        BackgroundColor = Color.FromArgb("#8C000000"),
        Padding = new Thickness(14, 6),
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.End,
        Margin = new Thickness(0, 0, 0, 28),
        IsVisible = false,
    };

    readonly TaskCompletionSource<bool> _camerasReady = new();
    bool _visible;
    bool _busy;
    bool _front = true;
    int _life;

    public LivenessPage()
    {
        Title = "Liveness";
        BackgroundColor = Colors.Black;

        var top = DeviceInfo.Current.Platform == DevicePlatform.iOS ? 54 : 16;
        _scoreHost = new Border
        {
            Content = _score,
            BackgroundColor = Color.FromArgb("#E6000000"),
            StrokeThickness = 0,
            Padding = new Thickness(16, 8),
            Margin = new Thickness(16, top + 40, 16, 0),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            IsVisible = false,
            InputTransparent = true,
        };

        _overlay = new GraphicsView
        {
            Drawable = _drawable,
            InputTransparent = true,
            BackgroundColor = Colors.Transparent,
        };

        _camera.CamerasLoaded += (_, _) => _camerasReady.TrySetResult(true);

        var flip = Icons.Circle("switchCamera", async () =>
        {
            _front = !_front;
            await RestartAsync();
        });
        flip.HorizontalOptions = LayoutOptions.End;
        flip.VerticalOptions = LayoutOptions.Start;

        var title = new Label
        {
            Text = "Liveness",
            TextColor = Colors.White,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(16, top, 16, 0),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
        };
        flip.Margin = new Thickness(16, top - 6, 16, 0);

        Content = new Grid { Children = { _camera, _overlay, title, _scoreHost, flip, _hint } };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        await StartAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        _life++;
        await StopAsync();
    }

    async Task RestartAsync()
    {
        _life++;
        await StopAsync();
        await StartAsync();
    }

    async Task StartAsync()
    {
        if (!_visible)
        {
            return;
        }

        await Task.WhenAny(_camerasReady.Task, Task.Delay(2500));
        var cameras = _camera.Cameras;
        if (cameras.Count == 0)
        {
            ShowHint("Camera unavailable");
            return;
        }

        var wanted = _front ? CameraPosition.Front : CameraPosition.Back;
        _camera.Camera = cameras.FirstOrDefault(c => c.Position == wanted) ?? cameras[0];
        await _camera.StartCameraAsync();
        ShowHint(SdkSession.Ready ? "" : SdkSession.Message);
        _ = LoopAsync(++_life);
    }

    async Task StopAsync()
    {
        // Clear the boxes before the camera goes, so a revisited tab never shows the last
        // visit's faces sitting over a preview that has not started yet.
        _drawable.Faces = Array.Empty<FaceBox>();
        _drawable.FrameWidth = 0;
        _drawable.FrameHeight = 0;
        _scoreHost.IsVisible = false;
        _overlay.Invalidate();
        try
        {
            await _camera.StopCameraAsync();
        }
        catch
        {
            // Already stopped.
        }
    }

    void ShowScores(IReadOnlyList<FaceBox> faces)
    {
        if (faces.Count == 0)
        {
            _scoreHost.IsVisible = false;
            return;
        }

        var threshold = SettingsStore.Current.LivenessThreshold;
        _score.Text = string.Join("\n", faces.Select(face =>
        {
            var score = face.Liveness ?? 0;
            var live = score >= threshold;
            return $"{(live ? "Live" : "Fake")}  {score:F2}";
        }));
        _score.TextColor = Colors.White;
        _scoreHost.IsVisible = true;
    }

    void ShowHint(string text)
    {
        _hint.Text = text;
        _hint.IsVisible = text.Length > 0;
    }

    async Task LoopAsync(int life)
    {
        while (_visible && life == _life)
        {
            if (_busy || !SdkSession.Ready)
            {
                if (!SdkSession.Ready)
                {
                    ShowHint(SdkSession.Message);
                }
                await Task.Delay(120);
                continue;
            }

            _busy = true;
            try
            {
                var jpeg = await CameraSnap.CaptureJpegAsync(_camera);
                if (jpeg is { Length: > 0 })
                {
                    // Liveness is the whole point of this screen, so it is the one estimator
                    // asked for.
                    var json = await FaceSdk.DetectAsync(jpeg, SettingsStore.Current.LivenessFlags);
                    var faces = FaceJson.ParseDetect(json);
                    var (w, h) = CaptureLogic.JpegSize(jpeg);
                    _drawable.Faces = faces;
                    _drawable.FrameWidth = w;
                    _drawable.FrameHeight = h;
                    // iOS preview X already matches the engine JPEG. Mirroring it puts the box
                    // on the other side of the face. Android's front preview is mirrored.
                    _drawable.Mirrored = _front && DeviceInfo.Current.Platform == DevicePlatform.Android;
                    _drawable.Threshold = SettingsStore.Current.LivenessThreshold;
                    _overlay.Invalidate();
                    ShowScores(faces);
                    ShowHint(faces.Count == 0 ? "No face" : "");
                }
            }
            catch (Exception ex)
            {
                // A dropped frame must not stop the loop, but the reason has to stay on screen.
                ShowHint(ex.Message);
            }
            finally
            {
                _busy = false;
            }

            await Task.Delay(60);
        }
    }
}
