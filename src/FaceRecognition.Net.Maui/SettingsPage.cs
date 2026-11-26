namespace FaceRecognition.Net.Maui;

/// <summary>
/// One Matching card in the order `fragment_settings.xml` lays it out — match threshold, the
/// Liveness check switch, Skip the capture review, then the liveness and pose sliders — and
/// then About. Reset is in the header, as on Android. Android `shell/SettingsFragment.kt`.
/// </summary>
public class SettingsPage : ContentPage
{
    /// <summary>Android reports this as a constant, not a value read from the engine.</summary>
    const string SdkVersion = "Face Recognition SDK";
    const string AppVersion = "1.0";

    readonly Label _license = new()
    {
        Text = "License: …",
        FontSize = 13,
        TextColor = DemoTheme.Muted,
    };

    readonly VerticalStackLayout _matching = new() { Spacing = 4 };

    public SettingsPage()
    {
        Title = "Settings";
        BackgroundColor = DemoTheme.Bg;
        Build();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _license.Text = "License: " + FaceJson.LicenseTier(await FaceSdk.GetLicenseStatusAsync());
        }
        catch
        {
            _license.Text = "License: Not licensed";
        }
    }

    void Build()
    {
        var reset = Icons.Button("reset", DemoTheme.Text, () =>
        {
            SettingsStore.Save(new AppSettings());
            Build();
        });

        var head = ShellUi.Header("Settings", null, reset);
        head.Padding = new Thickness(0, 0, 0, 4);

        var s = SettingsStore.Current;

        _matching.Children.Clear();
        _matching.Children.Add(new Label
        {
            Text = "Matching",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = DemoTheme.Text,
        });
        _matching.Children.Add(UnitSlider(
            "Match threshold",
            s.IdentifyThreshold,
            DemoSettings.MinThreshold,
            DemoSettings.MaxThreshold,
            v => SettingsStore.Save(SettingsStore.Current with { IdentifyThreshold = v })));
        _matching.Children.Add(Help(
            "The standard threshold for identity verification is 0.80. A gallery face matches at " +
            "or above this value. 0.70 is convenience, 0.90 is high security."));
        _matching.Children.Add(SwitchRow(
            "Liveness check",
            "Reject photos of photos and screens before identifying. A face counts as " +
            "live when its score is above 0.5.",
            s.CheckLiveness,
            v => SettingsStore.Save(SettingsStore.Current with { CheckLiveness = v })));
        _matching.Children.Add(SwitchRow(
            "Skip the capture review",
            "Enroll straight away when every check passes. A capture that fails one still opens " +
            "the review, so you can see why.",
            s.SkipReview,
            v => SettingsStore.Save(SettingsStore.Current with { SkipReview = v })));
        _matching.Children.Add(UnitSlider(
            "Liveness",
            s.LivenessThreshold,
            0,
            1,
            v => SettingsStore.Save(SettingsStore.Current with { LivenessThreshold = v })));
        _matching.Children.Add(AngleSlider(
            "Yaw",
            s.YawThreshold,
            v => SettingsStore.Save(SettingsStore.Current with { YawThreshold = v })));
        _matching.Children.Add(AngleSlider(
            "Roll",
            s.RollThreshold,
            v => SettingsStore.Save(SettingsStore.Current with { RollThreshold = v })));
        _matching.Children.Add(AngleSlider(
            "Pitch",
            s.PitchThreshold,
            v => SettingsStore.Save(SettingsStore.Current with { PitchThreshold = v })));

        var scroll = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Padding = new Thickness(0, 8, 0, 16),
                Children =
                {
                    ShellUi.CardView(_matching),
                    ShellUi.Section(
                        "About",
                        _license,
                        ShellUi.Row("SDK version", SdkVersion),
                        ShellUi.Row("App version", AppVersion)),
                    // The company mark closes the screen, now that About is no longer a tab.
                    new Image
                    {
                        Source = "fp_logo.png",
                        HeightRequest = 44,
                        Aspect = Aspect.AspectFit,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 32, 0, 24),
                    },
                },
            },
        };
        Content = new Grid
        {
            Padding = new Thickness(16, 4, 16, 0),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            Children = { head, scroll },
        };
        Grid.SetRow(scroll, 1);
    }

    static View Help(string text) => new Label
    {
        Text = text,
        FontSize = 12,
        TextColor = DemoTheme.Muted,
        Margin = new Thickness(0, 0, 0, 8),
    };

    static View SwitchRow(string label, string help, bool value, Action<bool> onChange)
    {
        var toggle = new Switch { IsToggled = value, OnColor = DemoTheme.Accent };
        toggle.Toggled += (_, e) => onChange(e.Value);

        var grid = new Grid
        {
            ColumnSpacing = 12,
            Margin = new Thickness(0, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        grid.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = label, FontSize = 14, TextColor = DemoTheme.Text },
                new Label { Text = help, FontSize = 12, TextColor = DemoTheme.Muted },
            },
        });
        grid.Add(toggle, 1);
        return grid;
    }

    static View UnitSlider(string label, double value, double min, double max, Action<double> onChange)
    {
        var shown = new Label
        {
            Text = ShellUi.Score(value),
            FontSize = 13,
            TextColor = DemoTheme.Muted,
        };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            MinimumTrackColor = DemoTheme.Accent,
        };
        slider.ValueChanged += (_, e) =>
        {
            // Two decimals, so the slider and the number beside it never disagree.
            var v = Math.Round(e.NewValue, 2);
            shown.Text = ShellUi.Score(v);
            onChange(v);
        };
        return LabelledSlider(label, shown, slider);
    }

    static View AngleSlider(string label, double value, Action<double> onChange)
    {
        var shown = new Label
        {
            Text = ((int)Math.Round(value)).ToString(),
            FontSize = 13,
            TextColor = DemoTheme.Muted,
        };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 90,
            Value = Math.Clamp(value, 0, 90),
            MinimumTrackColor = DemoTheme.Accent,
        };
        slider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue);
            shown.Text = ((int)v).ToString();
            onChange(v);
        };
        return LabelledSlider(label, shown, slider);
    }

    static View LabelledSlider(string label, Label shown, Slider slider)
    {
        var head = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        head.Add(new Label { Text = label, FontSize = 14, TextColor = DemoTheme.Text });
        head.Add(shown, 1);
        return new VerticalStackLayout { Spacing = 2, Children = { head, slider } };
    }
}
