using Camera.MAUI;
using Microsoft.Maui.Layouts;

namespace FaceRecognition.Net.Maui;

public class CapturePage : ContentPage
{
    const int HoldMs = 2000;
    const string AnimName = "capture_mode_anim";

    readonly CameraView _camera = new()
    {
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill,
        AutoStartPreview = false,
    };
    readonly CaptureOverlayDrawable _drawable = new();
    readonly GraphicsView _overlay;
    readonly Label _title = new()
    {
        Text = "Face Capture",
        TextColor = DemoTheme.Text,
        FontSize = 22,
        FontAttributes = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center,
    };
    readonly Label _hint = new()
    {
        Text = "",
        TextColor = DemoTheme.Danger,
        FontSize = 16,
        HorizontalTextAlignment = TextAlignment.End,
    };
    readonly Grid _hintHost = new() { InputTransparent = true };
    readonly AbsoluteLayout _stage = new();
    readonly View _back;
    readonly Button _enroll;
    readonly VerticalStackLayout _result = new()
    {
        IsVisible = false,
        Spacing = 0,
        BackgroundColor = Colors.Transparent,
        // Metrics-only pane — must not steal back-button hits (RN pointerEvents=box-none).
        InputTransparent = true,
    };
    readonly BoxView _resultSpacer = new() { Color = Colors.Transparent, InputTransparent = true };
    readonly List<FaceBox> _lastEyes = new();
    readonly Label _livenessLine = new() { TextColor = DemoTheme.Text, FontSize = 18, Margin = new Thickness(24, 16, 24, 0) };
    readonly Label _qualityLine = new() { TextColor = DemoTheme.Text, FontSize = 18, Margin = new Thickness(24, 16, 24, 0) };
    readonly Label _luminanceLine = new() { TextColor = DemoTheme.Text, FontSize = 18, Margin = new Thickness(24, 16, 24, 0) };
    readonly TaskCompletionSource _camerasReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    CaptureViewMode _viewMode = CaptureViewMode.NoFacePrepare;
    bool _visible;
    bool _looping;
    bool _holdForReview;
    bool _reviewOpen;
    bool _attached;
    bool _snapBusy;
    bool _front;
    bool _workerReady;
    bool _holding;
    DateTime? _holdStart;
    byte[]? _holdJpeg;
    byte[]? _lastJpeg;
    FaceBox? _holdFace;
    FaceBox? _capturedFace;
    FaceBox? _resultFace;
    int _life;
    int _holdGen;
    long _lastRaiseMs;
    double _frameW = 720;
    double _frameH = 1280;
    double _anim = 1.4;

    public CapturePage()
    {
        Title = "Capture";
        BackgroundColor = Colors.Black;
        Shell.SetBackgroundColor(this, DemoTheme.Bg);
        Shell.SetTitleColor(this, DemoTheme.Text);
        _back = DemoUi.BackButton(() => _ = GoHomeAsync());
        _enroll = new Button
        {
            Text = "Enroll",
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#63317B"),
            CornerRadius = 24,
            WidthRequest = 150,
            HeightRequest = 48,
            IsVisible = false,
        };
        _enroll.Clicked += async (_, _) => await EnrollHeldAsync();
        _overlay = new GraphicsView
        {
            Drawable = _drawable,
            InputTransparent = true,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
        _result.Add(_resultSpacer);
        _result.Add(_livenessLine);
        _result.Add(_qualityLine);
        _result.Add(_luminanceLine);

        _title.Margin = new Thickness(0, 16, 0, 0);
        _title.InputTransparent = true;
        AbsoluteLayout.SetLayoutBounds(_title, new Rect(0, 0, 1, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(_title, AbsoluteLayoutFlags.WidthProportional);
        _hint.HorizontalOptions = LayoutOptions.End;
        _hint.VerticalOptions = LayoutOptions.Start;
        _hint.Margin = new Thickness(20, 64, 20, 0);
        _hint.InputTransparent = true;
        _hintHost.Add(_hint);
        AbsoluteLayout.SetLayoutBounds(_hintHost, new Rect(0, 0, 1, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(_hintHost, AbsoluteLayoutFlags.WidthProportional);
        AbsoluteLayout.SetLayoutBounds(_result, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_result, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(_overlay, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(_overlay, AbsoluteLayoutFlags.All);
        // Enroll sits under the metrics block (square spacer ≈ stage width).
        AbsoluteLayout.SetLayoutBounds(_enroll, new Rect(0.5, 0, 150, 48));
        AbsoluteLayout.SetLayoutFlags(_enroll, AbsoluteLayoutFlags.XProportional);
        AbsoluteLayout.SetLayoutBounds(_back, new Rect(16, 52, 44, 44));
        AbsoluteLayout.SetLayoutFlags(_back, AbsoluteLayoutFlags.None);
        _livenessLine.InputTransparent = true;
        _qualityLine.InputTransparent = true;
        _luminanceLine.InputTransparent = true;
        _resultSpacer.InputTransparent = true;
        _stage.Children.Add(_overlay);
        _stage.Children.Add(_title);
        _stage.Children.Add(_hintHost);
        _stage.Children.Add(_result);
        _stage.Children.Add(_enroll);
        // Back last in tree so it stays tappable (RN/Flutter).
        _stage.Children.Add(_back);
        Content = _stage;
        Shell.SetNavBarIsVisible(this, false);
        _camera.CamerasLoaded += (_, _) => _camerasReady.TrySetResult();
        _overlay.SizeChanged += (_, _) => PaintOverlay();
        _stage.SizeChanged += (_, _) =>
        {
            if (_stage.Width > 0)
            {
                _resultSpacer.HeightRequest = _stage.Width;
                // spacer + 3×(16+~22) lines + gap ≈ width + 150
                AbsoluteLayout.SetLayoutBounds(_enroll, new Rect(0.5, _stage.Width + 150, 150, 48));
                AbsoluteLayout.SetLayoutFlags(_enroll, AbsoluteLayoutFlags.XProportional);
            }

            RaiseOverlays();
            PaintOverlay();
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        var life = ++_life;
        if (!_attached)
        {
            AbsoluteLayout.SetLayoutBounds(_camera, new Rect(0, 0, 1, 1));
            AbsoluteLayout.SetLayoutFlags(_camera, AbsoluteLayoutFlags.All);
            _stage.Children.Insert(0, _camera);
            _attached = true;
        }

        Dispatcher.Dispatch(async () =>
        {
            // A modal enroll sheet pops this page back into view. Starting the camera then
            // fights the sheet that is about to replace it.
            if (life == _life && !_holdForReview)
            {
                await StartAsync();
            }
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _visible = false;
        _workerReady = false;
        _life++;
        CancelHold();
        this.AbortAnimation(AnimName);
#if IOS
        NativeCaptureCoach.Detach(_stage);
#endif
        _ = FaceSdk.StopVideoWorkerAsync();
        if (_attached)
        {
            _ = _camera.StopCameraAsync();
        }
    }

    async Task StartAsync()
    {
        CancelHold();
        _workerReady = false;
        _result.IsVisible = false;
        _enroll.IsVisible = false;
        _overlay.IsVisible = true;
        _camera.IsVisible = true;
        _hint.Text = "";
        _holdJpeg = null;
        _lastJpeg = null;
        _capturedFace = null;
        _resultFace = null;
        ClearCapturedPreview();

        var permission = await Permissions.RequestAsync<Permissions.Camera>();
        if (permission != PermissionStatus.Granted)
        {
            _hint.Text = "Camera permission is required";
            return;
        }

        if (_camera.NumCamerasDetected == 0)
        {
            await Task.WhenAny(_camerasReady.Task, Task.Delay(2500));
        }

        _front = SettingsStore.Current.FrontCamera;
        var camera = _camera.Cameras.FirstOrDefault(c => _front
                ? c.Position == CameraPosition.Front
                : c.Position == CameraPosition.Back)
            ?? _camera.Cameras.FirstOrDefault();
        if (camera == null)
        {
            _hint.Text = "No camera found";
            return;
        }

        _camera.Camera = camera;
        // Same as Identify: Camera.MAUI iOS preview already matches engine X — only Android needs front mirror.
        _drawable.Mirror = _front && DeviceInfo.Current.Platform == DevicePlatform.Android;
        await FaceSdk.StopVideoWorkerAsync();
        if (await _camera.StartCameraAsync(new Size(1280, 720)) != CameraResult.Success)
        {
            await Task.Delay(500);
            if (await _camera.StartCameraAsync(new Size(1280, 720)) != CameraResult.Success)
            {
                _hint.Text = "Could not start the camera";
                return;
            }
        }

        RaiseOverlays();
        for (var i = 0; i < 8; i++)
        {
            await Task.Delay(150);
            RaiseOverlays();
            PaintOverlay();
        }

        while (SdkSession.Loading && _visible)
        {
            _hint.Text = SdkSession.Message;
            await Task.Delay(150);
        }
        if (!_visible)
        {
            return;
        }
        if (!SdkSession.Ready)
        {
            _hint.Text = SdkSession.Message;
            return;
        }

        // Native CaptureActivity / CaptureCameraViewController matchThreshold = 0.8.
        var code = await FaceSdk.StartVideoWorkerAsync(0.8f);
        _workerReady = code == FaceSdk.Success;
        if (!_workerReady)
        {
            _hint.Text = "VideoWorker failed (" + code + ")";
            return;
        }

        // Page is constructed already in NoFacePrepare, so a plain SetMode is a no-op
        // and the waiting brackets never animate into the face-in and capture stages.
        SetMode(CaptureViewMode.NoFacePrepare, force: true);
        RaiseOverlays();
        PaintOverlay();
        _ = LoopAsync(_life);
    }

    void RaiseOverlays()
    {
        void Apply()
        {
            _camera.ZIndex = 0;
            _overlay.ZIndex = 20;
            _title.ZIndex = 30;
            _hintHost.ZIndex = 30;
            _result.ZIndex = 40;
            _enroll.ZIndex = 45;
            _back.ZIndex = 50;
            _back.IsVisible = true;
#if IOS
            // Coach above camera only; title / hint / back stay above coach (no flicker).
            var host = NativeCaptureCoach.HostFrom(_stage);
            var cameraNative = _camera.Handler?.PlatformView as UIKit.UIView;
            var keepCoach = !_result.IsVisible
                || _viewMode is CaptureViewMode.FaceCapturePrepare or CaptureViewMode.FaceCaptureDone;
            if (host != null && host.Bounds.Width >= 2 && keepCoach)
            {
                _overlay.IsVisible = false;
                var coach = NativeCaptureCoach.Ensure(host, aboveCamera: cameraNative);
                coach.Apply(
                    _viewMode,
                    (float)_anim,
                    (float)_drawable.HoldProgress,
                    (int)Math.Round(_frameW),
                    (int)Math.Round(_frameH),
                    _drawable.Mirror,
                    _drawable.ShowPoseGuides,
                    (float)(_drawable.Yaw ?? 0),
                    (float)(_drawable.Pitch ?? 0));
                OverlayZOrder.BringToFront(_title, _hintHost, _back);
                if (_result.IsVisible)
                {
                    OverlayZOrder.BringToFront(_result, _enroll, _back);
                }

                return;
            }
#endif
            _overlay.IsVisible = true;
            OverlayZOrder.BringToFront(_overlay, _title, _hintHost, _back);
            if (_result.IsVisible)
            {
                OverlayZOrder.BringToFront(_result, _enroll, _back);
            }
        }

        if (MainThread.IsMainThread)
        {
            Apply();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Apply);
        }
    }

    void PaintOverlay()
    {
        void Apply()
        {
            if (_overlay.IsVisible)
            {
                _overlay.Invalidate();
            }
#if IOS
            var host = NativeCaptureCoach.HostFrom(_stage);
            var cameraNative = _camera.Handler?.PlatformView as UIKit.UIView;
            var keepCoach = !_result.IsVisible
                || _viewMode is CaptureViewMode.FaceCapturePrepare or CaptureViewMode.FaceCaptureDone;
            if (host != null && host.Bounds.Width >= 2 && keepCoach)
            {
                NativeCaptureCoach.Ensure(host, aboveCamera: cameraNative).Apply(
                    _viewMode,
                    (float)_anim,
                    (float)_drawable.HoldProgress,
                    (int)Math.Round(_frameW),
                    (int)Math.Round(_frameH),
                    _drawable.Mirror,
                    _drawable.ShowPoseGuides,
                    (float)(_drawable.Yaw ?? 0),
                    (float)(_drawable.Pitch ?? 0));
            }
#endif
        }

        if (MainThread.IsMainThread)
        {
            Apply();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(Apply);
        }
    }

    void SetMode(CaptureViewMode mode, bool force = false)
    {
        if (!force && _viewMode == mode)
        {
            return;
        }

        if (mode != CaptureViewMode.FaceCircle)
        {
            CancelHold();
        }

        _viewMode = mode;
        _drawable.ViewMode = mode;
        this.AbortAnimation(AnimName);
        StartModeAnim(mode);
        PaintOverlay();
    }

    void StartModeAnim(CaptureViewMode mode)
    {
        double from;
        double to;
        uint ms;
        var loop = false;
        switch (mode)
        {
            case CaptureViewMode.NoFacePrepare:
                from = 1.4;
                to = 0.88;
                ms = 800;
                break;
            case CaptureViewMode.RepeatNoFacePrepare:
                from = 0.88;
                to = 0.92;
                ms = 1300;
                loop = true;
                break;
            case CaptureViewMode.ToFaceCircle:
                from = 1.4;
                to = 0;
                ms = 800;
                break;
            case CaptureViewMode.FaceCircleToNoFace:
                from = 0;
                to = 1;
                ms = 600;
                break;
            case CaptureViewMode.FaceCapturePrepare:
                from = 0;
                to = 1;
                ms = 500;
                break;
            case CaptureViewMode.FaceCaptureDone:
                from = 0;
                to = 1;
                ms = 500;
                break;
            default:
                _anim = 0;
                _drawable.Anim = 0;
                PaintOverlay();
                return;
        }

        _anim = from;
        _drawable.Anim = from;
        if (loop)
        {
            // Android REPEAT_NO_FACE_PREPARE uses ValueAnimator.REVERSE — brackets breathe.
            CommitReverse(from, to, ms);
            return;
        }

        new Animation(v =>
        {
            _anim = v;
            _drawable.Anim = v;
            PaintOverlay();
        }, from, to).Commit(this, AnimName, 16, ms, Easing.CubicOut, (v, canceled) =>
        {
            if (!canceled)
            {
                Dispatcher.Dispatch(async () => await OnModeFinishedAsync(mode));
            }
        });
    }

    void CommitReverse(double from, double to, uint ms)
    {
        var forward = true;
        void Play(bool fwd)
        {
            if (_viewMode != CaptureViewMode.RepeatNoFacePrepare)
            {
                return;
            }

            new Animation(v =>
            {
                _anim = v;
                _drawable.Anim = v;
                PaintOverlay();
            }, fwd ? from : to, fwd ? to : from).Commit(this, AnimName, 16, ms, Easing.Linear, (_, canceled) =>
            {
                if (canceled || _viewMode != CaptureViewMode.RepeatNoFacePrepare)
                {
                    return;
                }

                forward = !fwd;
                Play(forward);
            });
        }

        Play(forward);
    }

    async Task OnModeFinishedAsync(CaptureViewMode mode)
    {
        switch (mode)
        {
            case CaptureViewMode.NoFacePrepare:
                SetMode(CaptureViewMode.RepeatNoFacePrepare);
                break;
            case CaptureViewMode.ToFaceCircle:
                SetMode(CaptureViewMode.FaceCircle);
                break;
            case CaptureViewMode.FaceCircleToNoFace:
                SetMode(CaptureViewMode.NoFacePrepare);
                break;
            case CaptureViewMode.FaceCapturePrepare:
                SetMode(CaptureViewMode.FaceCaptureDone);
                break;
            case CaptureViewMode.FaceCaptureDone:
                // The camera and the worker stay up while the still is detected: tearing them
                // down first left the preview dead for the length of the call, and the review
                // that follows replaces the screen anyway.
                _workerReady = false;
                await FinishWithDetectAsync();
                break;
        }
    }

    async Task LoopAsync(int life)
    {
        if (_looping)
        {
            return;
        }

        _looping = true;
        var lastEyesMs = 0L;
        try
        {
            while (_visible && life == _life)
            {
                // Poll worker events first — SdkGate is serial; heavy Detect must not starve tracking.
                for (var i = 0; i < 8; i++)
                {
                    var json = await FaceSdk.PollEventAsync();
                    if (string.IsNullOrEmpty(json))
                    {
                        break;
                    }

                    OnWorkerEvent(json);
                }

                TickHold();
                // Keep coach above Camera.MAUI — preview can reclaim top z-order while running.
                var nowRaise = Environment.TickCount64;
                if (nowRaise - _lastRaiseMs >= 400)
                {
                    _lastRaiseMs = nowRaise;
                    RaiseOverlays();
                }

                var mode = _viewMode;
                var showingResult = _result.IsVisible;
                if (_workerReady
                    && !_snapBusy
                    && !showingResult
                    && mode != CaptureViewMode.NoFacePrepare
                    && mode != CaptureViewMode.FaceCaptureDone
                    && mode != CaptureViewMode.FaceCapturePrepare)
                {
                    _snapBusy = true;
                    try
                    {
                        var jpeg = await SnapAsync();
                        if (jpeg != null)
                        {
                            var live = await FaceSdk.IngestLiveCameraFrameAsync(jpeg, _front);
                            if (live.Ingested && live.Width > 0 && live.Height > 0)
                            {
                                _frameW = live.Width;
                                _frameH = live.Height;
                                _drawable.FrameWidth = live.Width;
                                _drawable.FrameHeight = live.Height;
                            }

                            if (live.Ingested)
                            {
                                try
                                {
                                    var exported = await FaceSdk.ExportLastLiveFrameAsync();
                                    if (exported.Ingested && exported.Jpeg is { Length: > 0 })
                                    {
                                        _lastJpeg = exported.Jpeg;
                                        var now = Environment.TickCount64;
                                        // Eyes sidecar at most ~2 Hz so VideoWorker keeps getting frames.
                                        if (now - lastEyesMs >= 500
                                            && mode is CaptureViewMode.FaceCircle
                                                or CaptureViewMode.RepeatNoFacePrepare)
                                        {
                                            lastEyesMs = now;
                                            try
                                            {
                                                var eyeJson = await FaceSdk.DetectAsync(
                                                    exported.Jpeg,
                                                    FaceSdk.DetectEyes);
                                                var eyes = FaceJson.ParseDetect(eyeJson);
                                                if (eyes.Count > 0)
                                                {
                                                    _lastEyes.Clear();
                                                    _lastEyes.AddRange(eyes);
                                                }
                                            }
                                            catch
                                            {
                                            }
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        _snapBusy = false;
                    }
                }

                await Task.Delay(50);
            }
        }
        finally
        {
            _looping = false;
        }
    }

    void OnWorkerEvent(string json)
    {
        var mode = _viewMode;
        if (mode is CaptureViewMode.FaceCaptureDone or CaptureViewMode.NoFacePrepare)
        {
            return;
        }

        var tracking = WorkerJson.ParseTracking(json);
        if (tracking == null)
        {
            return;
        }

        if (tracking.Value.FrameWidth > 0 && tracking.Value.FrameHeight > 0)
        {
            _frameW = tracking.Value.FrameWidth;
            _frameH = tracking.Value.FrameHeight;
            _drawable.FrameWidth = _frameW;
            _drawable.FrameHeight = _frameH;
        }

        var boxes = tracking.Value.Faces
            .Where(f => !f.Weak)
            .Select(WorkerJson.ToFaceBox)
            .ToList();
        boxes = CaptureLogic.MergeEyes(boxes, _lastEyes, swapLeftRight: _front);
        var state = CaptureLogic.CheckFace(boxes, SettingsStore.Current, _frameW, _frameH);
        var face = boxes.Count >= 1 ? boxes[0] : null;

        if (mode == CaptureViewMode.RepeatNoFacePrepare)
        {
            if (state != CaptureState.NoFace)
            {
                SetMode(CaptureViewMode.ToFaceCircle);
            }

            return;
        }

        if (mode != CaptureViewMode.FaceCircle)
        {
            return;
        }

        if (state == CaptureState.NoFace)
        {
            _hint.Text = "";
            _drawable.ShowPoseGuides = false;
            CancelHold();
            SetMode(CaptureViewMode.FaceCircleToNoFace);
            return;
        }

        if (face != null)
        {
            // Native CaptureView keeps yaw/pitch guides whenever faceBoxes are set in FACE_CIRCLE.
            _drawable.ShowPoseGuides = true;
            _drawable.Yaw = face.Yaw;
            _drawable.Pitch = face.Pitch;
        }
        else
        {
            _drawable.ShowPoseGuides = false;
        }

        if (state == CaptureState.CaptureOk && face != null)
        {
            _holdFace = face;
            _hint.Text = "";
            _ = RememberHoldFrameAsync();
            StartHold();
            PaintOverlay();
            return;
        }

        CancelHold();
        _hint.Text = CaptureLogic.WarningFor(state);
        PaintOverlay();
    }

    void StartHold()
    {
        if (_holding)
        {
            return;
        }

        _holding = true;
        _holdStart = DateTime.UtcNow;
    }

    void TickHold()
    {
        if (!_holding || _holdStart == null || _viewMode != CaptureViewMode.FaceCircle)
        {
            return;
        }

        var progress = Math.Clamp((DateTime.UtcNow - _holdStart.Value).TotalMilliseconds / HoldMs, 0, 1);
        _drawable.HoldProgress = progress;
        PaintOverlay();
        if (progress >= 1)
        {
            _holding = false;
            _ = FinishHoldAsync();
        }
    }

    void CancelHold()
    {
        _holdGen++;
        _holding = false;
        _holdStart = null;
        _holdJpeg = null;
        _holdFace = null;
        _drawable.HoldProgress = 0;
    }

    async Task RememberHoldFrameAsync()
    {
        var gen = _holdGen;
        try
        {
            var exported = await FaceSdk.ExportLastLiveFrameAsync();
            if (gen != _holdGen || _viewMode != CaptureViewMode.FaceCircle)
            {
                return;
            }

            if (!exported.Ingested || exported.Jpeg == null || exported.Jpeg.Length == 0)
            {
                return;
            }

            _holdJpeg = exported.Jpeg;
            _lastJpeg = exported.Jpeg;
            if (exported.Width > 0 && exported.Height > 0)
            {
                _frameW = exported.Width;
                _frameH = exported.Height;
                _drawable.FrameWidth = _frameW;
                _drawable.FrameHeight = _frameH;
            }
        }
        catch
        {
        }
    }

    async Task FinishHoldAsync()
    {
        if (_viewMode != CaptureViewMode.FaceCircle)
        {
            return;
        }

        var gen = _holdGen;
        await RememberHoldFrameAsync();
        if (gen != _holdGen || _viewMode != CaptureViewMode.FaceCircle)
        {
            return;
        }

        var jpeg = _holdJpeg ?? _lastJpeg;
        var face = _holdFace;
        if (jpeg == null || face == null)
        {
            CancelHold();
            return;
        }

        _lastJpeg = jpeg;
        _capturedFace = face;
        _hint.Text = "";
        // iOS: setCapturedBitmap then faceCapturePrepare — keep camera until FaceCaptureDone.
        ApplyCapturedPreview(jpeg);
        SetMode(CaptureViewMode.FaceCapturePrepare);
    }

    void ApplyCapturedPreview(byte[] jpeg)
    {
        // Crop roiRect1 + mirror for front (engine JPEG is unmirrored; live preview is mirrored).
        var (jw, jh) = CaptureLogic.JpegSize(jpeg);
        if (jw > 0 && jh > 0)
        {
            _frameW = jw;
            _frameH = jh;
            _drawable.FrameWidth = jw;
            _drawable.FrameHeight = jh;
        }

        // Android CaptureView flips ROI for front; iOS Camera.MAUI already matches engine X.
        var mirrorPreview = _front && DeviceInfo.Current.Platform == DevicePlatform.Android;
        _drawable.CapturedImage = CaptureRoiPreview.MakeImage(jpeg, _frameW, _frameH, mirror: mirrorPreview);
#if IOS
        var host = NativeCaptureCoach.HostFrom(_stage);
        var cameraNative = _camera.Handler?.PlatformView as UIKit.UIView;
        if (host != null)
        {
            NativeCaptureCoach.Ensure(host, aboveCamera: cameraNative).SetCapturedJpeg(
                jpeg,
                (int)Math.Round(_frameW),
                (int)Math.Round(_frameH),
                mirror: mirrorPreview);
        }
#endif
        PaintOverlay();
    }

    void ClearCapturedPreview()
    {
        _drawable.CapturedImage = null;
#if IOS
        var host = NativeCaptureCoach.HostFrom(_stage);
        if (host != null)
        {
            NativeCaptureCoach.Ensure(host).SetCapturedJpeg(null, (int)_frameW, (int)_frameH, mirror: false);
        }
#endif
    }

    async Task FinishWithDetectAsync()
    {
        var jpeg = _lastJpeg;
        var fallback = _capturedFace;
        if (jpeg == null)
        {
            ShowResult(fallback, null);
            return;
        }

        FaceBox? shown = fallback;
        var count = shown == null ? 0 : 1;
        try
        {
            // Android FaceDetectionParam.allAttributes() + check_liveness_level.
            var detect = await FaceSdk.DetectAsync(jpeg, SettingsStore.Current.AttributeFlags);
            var boxes = FaceJson.ParseDetect(detect);
            count = boxes.Count;
            if (boxes.Count >= 1)
            {
                shown = boxes[0];
            }
        }
        catch
        {
        }

        _holdJpeg = jpeg;
        _holdFace = shown;
        _resultFace = shown;
        await PresentEnrollAsync(count);
    }

    void ShowResult(FaceBox? shown, byte[]? jpeg)
    {
        _resultFace = shown;
        _holdFace = shown;
        _holdJpeg = jpeg ?? _lastJpeg;
        if (_stage.Width > 0)
        {
            _resultSpacer.HeightRequest = _stage.Width;
            AbsoluteLayout.SetLayoutBounds(_enroll, new Rect(0.5, _stage.Width + 150, 150, 48));
            AbsoluteLayout.SetLayoutFlags(_enroll, AbsoluteLayoutFlags.XProportional);
        }

        if (shown != null)
        {
            var still = CaptureLogic.CheckFace([shown], SettingsStore.Current, _frameW, _frameH);
            if (still == CaptureState.FaceOccluded)
            {
                _hint.Text = "Face occluded!";
            }
            else if (still == CaptureState.EyeClosed)
            {
                _hint.Text = "Eye closed!";
            }

            var label = (shown.LivenessLabel ?? "").ToLowerInvariant();
            var score = shown.Liveness ?? 0;
            var spoof = label.Contains("spoof") || label.Contains("fake") || score < SettingsStore.Current.LivenessThreshold;
            _livenessLine.TextColor = DemoTheme.Text;
            _livenessLine.Text = spoof
                ? "Liveness: Spoof, score = " + score
                : "Liveness: Real, score = " + score;
            _qualityLine.Text = QualityText(shown.FaceQuality) + (string.IsNullOrEmpty(shown.QualityLabel) ? "" : "\n" + shown.QualityLabel);
            _luminanceLine.Text = "Luminance: " + (shown.FaceLuminance ?? 0);
        }
        else
        {
            _livenessLine.Text = "No face attributes";
            _qualityLine.Text = "";
            _luminanceLine.Text = "";
        }

        _camera.IsVisible = false;
        _result.IsVisible = true;
        _enroll.IsVisible = true;
        _back.IsVisible = true;
        // Stay in FaceCaptureDone so the oval face preview remains.
        _viewMode = CaptureViewMode.FaceCaptureDone;
        _drawable.ViewMode = CaptureViewMode.FaceCaptureDone;
        _anim = 1;
        _drawable.Anim = 1;
        // Back must be last in the MAUI tree and native z-order.
        if (_stage.Children.Contains(_back))
        {
            _stage.Children.Remove(_back);
            _stage.Children.Add(_back);
        }

        RaiseOverlays();
        PaintOverlay();
        OverlayZOrder.BringToFront(_result, _enroll, _back);
    }

    async Task GoHomeAsync()
    {
        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync();
        }
    }

    static string QualityText(double? score)
    {
        var s = score ?? 0;
        if (s < 0.5)
        {
            return "Low · " + Math.Round(s * 100) + "%";
        }

        if (s < 0.75)
        {
            return "Medium · " + Math.Round(s * 100) + "%";
        }

        return "High · " + Math.Round(s * 100) + "%";
    }

    async Task PresentEnrollAsync(int? knownCount = null)
    {
        if (_reviewOpen)
        {
            return;
        }

        var jpeg = _holdJpeg ?? _lastJpeg;
        var face = _holdFace ?? _resultFace ?? _capturedFace;
        if (jpeg == null)
        {
            ShowResult(face, jpeg);
            return;
        }

        _reviewOpen = true;
        try
        {
            var count = knownCount;
            var shown = face;
            if (count == null)
            {
                var detected = FaceJson.ParseDetect(await FaceSdk.DetectWithTemplatesAsync(
                    jpeg, SettingsStore.Current.EnrollFlags, 1, true));
                count = detected.Count;
                if (detected.Count > 0)
                {
                    shown = detected[0];
                }
            }

            var blocked = count == 1 ? null : count == 0
                ? "No face found. Use a clear, front-facing photo."
                : $"{count} faces found. Use a photo that shows only one person.";

            if (SettingsStore.Current.SkipReview && blocked == null)
            {
                var quick = await FaceGallery.EnrollAsync("", "", jpeg, DemoSettings.Limits);
                if (quick is EnrollOutcome.Enrolled fast)
                {
                    await ShowEnrolledAsync(fast);
                    return;
                }
            }

            var review = new ReviewPage(jpeg, shown, count.Value, blocked);
            _holdForReview = true;
            await Navigation.PushModalAsync(review);
            var enrolled = await review.Completion;
            if (!enrolled || review.Enrolled is not { } done)
            {
                _holdForReview = false;
                if (_visible)
                {
                    await StartAsync();
                }

                return;
            }

            await ShowEnrolledAsync(done);
        }
        catch (Exception ex)
        {
            _holdForReview = false;
            await DisplayAlert("Capture", ex.Message, "OK");
        }
        finally
        {
            _reviewOpen = false;
        }
    }

    async Task ShowEnrolledAsync(EnrollOutcome.Enrolled done)
    {
        var reason = done.Duplicates.Count == 0
            ? null
            : "This face also matches " + string.Join(
                ", ",
                done.Duplicates.Select(c =>
                    $"{c.Name} ({ShellUi.IdTag(c.Id)}) at {ShellUi.Score(c.Similarity)}")) +
              ". Enroll again only if it is the same person.";
        var status = new StatusPage(
            $"Enrolled {done.Record.Name} as {ShellUi.IdTag(done.Record.Id)}",
            reason,
            done.Report);
        var closed = new TaskCompletionSource<bool>();
        status.Disappearing += (_, _) => closed.TrySetResult(true);
        _holdForReview = true;
        await Navigation.PushModalAsync(status);
        await closed.Task;
        await GoHomeAsync();
    }

    async Task EnrollHeldAsync() => await PresentEnrollAsync();

    Task<byte[]?> SnapAsync() => CameraSnap.CaptureJpegAsync(_camera);
}
