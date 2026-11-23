namespace FaceRecognition.Net.Maui;

/// <summary>
/// The capture review: the still that was taken, what the checks said about it, and an optional
/// name before it is enrolled. It is the same full-height sheet the enrolment status uses, so
/// one replaces the other rather than stacking. Dismissing it is a retake, not a silent exit —
/// nothing has been enrolled yet.
/// </summary>
public class ReviewPage : ContentPage
{
    readonly Entry _name = new()
    {
        Placeholder = "Leave blank to name after the ID",
        TextColor = DemoTheme.Text,
        PlaceholderColor = DemoTheme.Muted,
        BackgroundColor = ShellUi.Card,
    };

    readonly Entry _note = new()
    {
        Placeholder = "Note (optional)",
        TextColor = DemoTheme.Text,
        PlaceholderColor = DemoTheme.Muted,
        BackgroundColor = ShellUi.Card,
    };

    readonly byte[] _jpeg;
    readonly TaskCompletionSource<bool> _done = new();

    /// <summary>Resolves true once the face was enrolled, false when the user asked to retake.</summary>
    public Task<bool> Completion => _done.Task;

    /// <summary>Set when Enroll succeeded. The capture page shows the status after this sheet closes.</summary>
    public EnrollOutcome.Enrolled? Enrolled { get; private set; }

    public ReviewPage(byte[] jpeg, FaceBox? face, int faceCount, string? blockReason)
    {
        _jpeg = jpeg;
        Title = "Capture review";
        BackgroundColor = DemoTheme.Bg;

        var shot = new Image
        {
            Source = ImageSource.FromStream(() => new MemoryStream(jpeg)),
            HeightRequest = 220,
            Aspect = Aspect.AspectFit,
            BackgroundColor = Colors.Black,
        };

        var ready = blockReason == null;
        var head = ShellUi.Header(
            "Capture review",
            ShellUi.Back(this, () => FinishAsync(false)),
            ShellUi.Pill(ready ? "Ready to enroll" : "Retake needed",
                ready ? ShellUi.StatusMatch : ShellUi.Danger));

        var checks = new List<View>
        {
            ShellUi.Row("Faces", ShellUi.FacesCount(faceCount),
                (faceCount == 1 ? "1" : $"{faceCount}", faceCount == 1 ? ShellUi.Badge.Pass : ShellUi.Badge.Fail)),
        };
        if (face != null)
        {
            checks.Add(ShellUi.Row("Liveness", ShellUi.Score(face.Liveness ?? double.NaN),
                (DemoSettings.IsLive(face.Liveness) ? "Live" : "Check",
                 DemoSettings.IsLive(face.Liveness) ? ShellUi.Badge.Pass : ShellUi.Badge.Warn),
                face.Liveness));
            checks.Add(ShellUi.Row("Yaw", ShellUi.Angle(face.Yaw ?? double.NaN)));
            checks.Add(ShellUi.Row("Roll", ShellUi.Angle(face.Roll ?? double.NaN)));
            checks.Add(ShellUi.Row("Pitch", ShellUi.Angle(face.Pitch ?? double.NaN)));
        }

        var retake = new Button
        {
            Text = "Retake",
            TextColor = DemoTheme.Accent,
            BackgroundColor = Colors.Transparent,
            BorderColor = ShellUi.Outline,
            BorderWidth = 1,
            CornerRadius = 24,
            HeightRequest = 48,
        };
        retake.Clicked += async (_, _) => await FinishAsync(false);

        var enroll = new Button
        {
            Text = "Enroll",
            TextColor = Colors.White,
            BackgroundColor = DemoTheme.Tile,
            CornerRadius = 24,
            HeightRequest = 48,
            IsEnabled = ready,
        };
        enroll.Clicked += async (_, _) => await EnrollAsync(enroll);

        var buttons = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
        };
        buttons.Add(retake);
        buttons.Add(enroll, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 8, 16, 16),
            Children = { shot, ShellUi.Section("Capture checks", checks.ToArray()) },
        };

        if (blockReason != null)
        {
            body.Children.Add(new Label
            {
                Text = blockReason,
                FontSize = 13,
                TextColor = ShellUi.Danger,
            });
        }

        body.Children.Add(new Label { Text = "Name", FontSize = 13, TextColor = DemoTheme.Muted });
        body.Children.Add(_name);
        body.Children.Add(_note);
        body.Children.Add(buttons);

        head.Padding = new Thickness(4, 4, 8, 0);
        var scroll = new ScrollView { Content = body };
        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Children = { head, scroll },
        };
        Grid.SetRow(scroll, 1);
    }

    async Task EnrollAsync(Button enroll)
    {
        enroll.IsEnabled = false;
        var outcome = await FaceGallery.EnrollAsync(
            _name.Text ?? "",
            _note.Text ?? "",
            _jpeg,
            DemoSettings.Limits);

        if (outcome is EnrollOutcome.Enrolled done)
        {
            // Close this sheet and stop. Pushing the status here, while the capture page also
            // navigates, dismisses the whole app.
            Enrolled = done;
            await FinishAsync(true);
            return;
        }

        if (outcome is EnrollOutcome.Rejected bad)
        {
            enroll.IsEnabled = true;
            await Navigation.PushModalAsync(new StatusPage("Enrollment stopped", bad.Reason, bad.Report));
        }
    }

    async Task FinishAsync(bool enrolled)
    {
        if (Navigation.ModalStack.Contains(this))
        {
            await Navigation.PopModalAsync();
        }

        _done.TrySetResult(enrolled);
    }

    protected override bool OnBackButtonPressed()
    {
        _done.TrySetResult(false);
        return base.OnBackButtonPressed();
    }
}
