using Camera.MAUI;

namespace FaceRecognition.Net.Maui;

/// <summary>
/// The Identity and Attribute tabs, which are one screen on Android too
/// (<c>IdentifyFragment</c> / <c>IdentifyFragment.attribute()</c>): the mode only changes what
/// the shutter runs and which result screen it opens.
///
/// Laid out like <c>fragment_identify.xml</c> — a title and the gallery count above the
/// preview; the status chip at the top of it, the threshold chip bottom-left and the camera
/// flip bottom-right; then Gallery, the shutter and Auto along the bottom.
/// </summary>
public class CameraTabPage : ContentPage
{
    /// <summary>Auto fires once one good face has stayed in place this long.</summary>
    const int AutoStableMs = 300;

    /// <summary>The five states Android coaches with — not the enrolment gate's nine.</summary>
    enum Status { NoFace, Many, MoveCloser, TooDark, Ok }

    readonly bool _attributeMode;

    readonly CameraView _camera = new()
    {
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill,
    };

    readonly Label _galleryCount = new()
    {
        FontSize = 13,
        TextColor = DemoTheme.Muted,
        VerticalOptions = LayoutOptions.Center,
        HorizontalOptions = LayoutOptions.End,
    };

    readonly Border _statusChip;
    readonly Label _statusText = new() { FontSize = 14, TextColor = Colors.White };
    readonly Border _thresholdChip;
    readonly Label _thresholdText = new() { FontSize = 12, TextColor = Colors.White };

    readonly ActivityIndicator _busy = new()
    {
        IsVisible = false,
        IsRunning = false,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
    };

    readonly Switch _auto = new() { OnColor = DemoTheme.Accent };
    readonly Button _shutter;

    readonly TaskCompletionSource<bool> _camerasReady = new();
    bool _visible;
    bool _busyRun;
    bool _front = true;
    int _life;
    byte[]? _lastJpeg;
    Status _status = Status.NoFace;
    (double X, double Y)? _lastCentre;
    long _stableSince;

    public CameraTabPage(bool attributeMode = false)
    {
        _attributeMode = attributeMode;
        Title = attributeMode ? "Attribute" : "Identity";
        BackgroundColor = DemoTheme.Bg;

        _statusText.Text = SdkSession.Message;
        _statusChip = Chip(_statusText, Color.FromArgb("#8C000000"));
        _statusChip.HorizontalOptions = LayoutOptions.Center;
        _statusChip.VerticalOptions = LayoutOptions.Start;
        _statusChip.Margin = new Thickness(0, 12, 0, 0);

        _thresholdChip = Chip(_thresholdText, Color.FromArgb("#8C000000"));
        _thresholdChip.HorizontalOptions = LayoutOptions.Start;
        _thresholdChip.VerticalOptions = LayoutOptions.End;
        _thresholdChip.Margin = new Thickness(12, 0, 0, 12);

        var flip = Icons.Circle("switchCamera", async () =>
        {
            _front = !_front;
            await RestartAsync();
        });
        flip.HorizontalOptions = LayoutOptions.End;
        flip.VerticalOptions = LayoutOptions.End;
        flip.Margin = new Thickness(0, 0, 12, 12);

        _camera.CamerasLoaded += (_, _) => _camerasReady.TrySetResult(true);

        var stage = new Grid
        {
            BackgroundColor = Colors.Black,
            Children = { _camera, _statusChip, _thresholdChip, flip, _busy },
        };

        _shutter = new Button
        {
            Text = "●",
            FontSize = 30,
            TextColor = Colors.White,
            BackgroundColor = DemoTheme.Tile,
            CornerRadius = 34,
            WidthRequest = 68,
            HeightRequest = 68,
            Padding = 0,
        };
        _shutter.Clicked += async (_, _) => await FireAsync();

        var gallery = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                Icons.Glyph("gallery", DemoTheme.Text, 24),
                new Label { Text = "Gallery", FontSize = 12, TextColor = DemoTheme.Text },
            },
        };
        var galleryTap = new TapGestureRecognizer();
        galleryTap.Tapped += async (_, _) => await PickAsync();
        gallery.GestureRecognizers.Add(galleryTap);

        _auto.IsToggled = DemoSettings.AutoCapture;
        _auto.Toggled += (_, e) =>
        {
            DemoSettings.AutoCapture = e.Value;
            _stableSince = 0;
        };
        var auto = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                _auto,
                new Label { Text = "Auto", FontSize = 12, TextColor = DemoTheme.Text },
            },
        };

        var bottom = new Grid
        {
            Padding = new Thickness(24, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
        };
        bottom.Add(gallery);
        bottom.Add(_shutter, 1);
        bottom.Add(auto, 2);

        var header = ShellUi.Header(Title, null, _galleryCount);
        header.Padding = new Thickness(16, 4, 12, 8);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            Children = { header, stage, bottom },
        };
        Grid.SetRow(stage, 1);
        Grid.SetRow(bottom, 2);
    }

    static Border Chip(Label content, Color background) => new()
    {
        Content = content,
        BackgroundColor = background,
        StrokeThickness = 0,
        Padding = new Thickness(14, 6),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
    };

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        Refresh();
        FaceGallery.Changed += Refresh;
        await StartAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        _life++;
        FaceGallery.Changed -= Refresh;
        // The coach resets with the camera, so coming back never shows the last reading.
        _stableSince = 0;
        _lastCentre = null;
        SetStatus(Status.NoFace);
        await StopAsync();
    }

    void Refresh()
    {
        var count = FaceGallery.Count();
        _galleryCount.Text = ShellUi.FacesCount(count);
        _thresholdText.Text = $"Threshold {ShellUi.Score(DemoSettings.Limits.Match)}";
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
            SetStatus(Status.NoFace);
            return;
        }
        var wanted = _front ? CameraPosition.Front : CameraPosition.Back;
        _camera.Camera = cameras.FirstOrDefault(c => c.Position == wanted) ?? cameras[0];
        await _camera.StartCameraAsync();
        _ = LoopAsync(++_life);
    }

    async Task StopAsync()
    {
        try
        {
            await _camera.StopCameraAsync();
        }
        catch
        {
            // Already stopped.
        }
    }

    /// <summary>
    /// Android `IdentifyFragment.analyze`: a box-only detect, five states, and Auto once the
    /// face has held still. Nothing here asks for liveness or pose — the result screen does.
    /// </summary>
    async Task LoopAsync(int life)
    {
        while (_visible && life == _life)
        {
            if (!SdkSession.Ready)
            {
                _statusText.Text = SdkSession.Message;
                _statusChip.BackgroundColor = Color.FromArgb("#8C000000");
                await Task.Delay(200);
                continue;
            }

            try
            {
                var jpeg = await CameraSnap.CaptureJpegAsync(_camera);
                if (jpeg is { Length: > 0 })
                {
                    _lastJpeg = jpeg;
                    var faces = FaceJson.ParseDetect(
                        await FaceSdk.DetectAsync(jpeg, FaceSdk.DetectPose));
                    var (w, _) = CaptureLogic.JpegSize(jpeg);
                    var status = StatusFor(faces, w);
                    Track(faces, status, w);
                    // The chip reports the search while one is running; the coach keeps
                    // reading frames behind it rather than freezing on the last one.
                    if (!_busyRun)
                    {
                        SetStatus(status);
                    }

                    if (status == Status.Ok
                        && DemoSettings.AutoCapture
                        && !_busyRun
                        && _stableSince != 0
                        && Environment.TickCount64 - _stableSince >= AutoStableMs)
                    {
                        _stableSince = 0;
                        await FireAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                // A dropped frame must not stop the loop, but the reason has to stay on screen.
                _statusText.Text = ex.Message;
                _statusChip.BackgroundColor = Color.FromArgb("#8C000000");
            }

            await Task.Delay(60);
        }
    }

    static Status StatusFor(IReadOnlyList<FaceBox> faces, double frameWidth)
    {
        if (faces.Count == 0)
        {
            return Status.NoFace;
        }
        if (faces.Count > 1)
        {
            return Status.Many;
        }
        var face = faces[0];
        // Face width against the frame width, which is what the native identify screen uses.
        if (frameWidth > 0 && face.X2 - face.X1 < frameWidth * 0.28)
        {
            return Status.MoveCloser;
        }
        var luminance = face.FaceLuminance ?? 0;
        if (luminance > 0.001 && luminance < 0.25)
        {
            return Status.TooDark;
        }
        return Status.Ok;
    }

    void Track(IReadOnlyList<FaceBox> faces, Status status, double frameWidth)
    {
        if (status != Status.Ok || faces.Count == 0)
        {
            _stableSince = 0;
            _lastCentre = null;
            return;
        }
        var face = faces[0];
        var centre = ((face.X1 + face.X2) / 2, (face.Y1 + face.Y2) / 2);
        var moved = _lastCentre is not { } prev
            || Math.Abs(prev.X - centre.Item1) > frameWidth * 0.06
            || Math.Abs(prev.Y - centre.Item2) > frameWidth * 0.06;
        if (moved || _stableSince == 0)
        {
            _stableSince = Environment.TickCount64;
        }
        _lastCentre = centre;
    }

    void SetStatus(Status status)
    {
        _status = status;
        _statusText.Text = status switch
        {
            Status.NoFace => "No face",
            Status.Many => "More than one face",
            Status.MoveCloser => "Move closer",
            Status.TooDark => "Too dark",
            _ => "Face found",
        };
        _statusChip.BackgroundColor = status == Status.Ok
            ? ShellUi.StatusMatch
            : Color.FromArgb("#8C000000");
    }

    async Task PickAsync()
    {
        try
        {
            var picked = await GalleryPhotos.PickAsync(multiple: false);
            if (picked.Count == 0 || picked[0].Bytes.Length == 0)
            {
                return;
            }
            await RunAsync(GalleryImagePrep.PrepareEngineJpeg(picked[0].Bytes));
        }
        catch (Exception ex)
        {
            await DisplayAlert(Title, ex.Message, "OK");
        }
    }

    async Task FireAsync()
    {
        if (_busyRun)
        {
            return;
        }
        var jpeg = _lastJpeg;
        if (jpeg == null || _status == Status.NoFace)
        {
            await DisplayAlert(Title, FaceGallery.NoFace, "OK");
            return;
        }
        await RunAsync(jpeg);
    }

    async Task RunAsync(byte[] jpeg)
    {
        SetBusy(true);
        try
        {
            if (_attributeMode)
            {
                var attributes = await FaceGallery.AttributesAsync(jpeg, DemoSettings.Limits);
                await Navigation.PushAsync(new AttributeResultPage(attributes));
                return;
            }

            var outcome = await FaceGallery.IdentifyAsync(
                jpeg, DemoSettings.Limits, DemoSettings.LivenessOn);
            if (outcome is IdentifyOutcome.Done done)
            {
                await Navigation.PushAsync(new ResultPage(done.Result));
            }
            else if (outcome is IdentifyOutcome.Rejected bad)
            {
                await Navigation.PushModalAsync(
                    new StatusPage("Identify stopped", bad.Reason, bad.Report));
            }
        }
        finally
        {
            SetBusy(false);
            _stableSince = 0;
        }
    }

    void SetBusy(bool on)
    {
        _busyRun = on;
        _busy.IsVisible = on;
        _busy.IsRunning = on;
        _shutter.IsEnabled = !on;
        if (on)
        {
            _statusText.Text = _attributeMode ? "Reading attributes" : "Identifying";
        }
    }
}
