using Microsoft.Maui.Controls.Shapes;

namespace FaceRecognition.Net.Maui;

static class DemoTheme
{
    public static readonly Color Bg = Color.FromArgb("#1C1B1F");
    public static readonly Color Tile = Color.FromArgb("#63317B");
    public static readonly Color Text = Color.FromArgb("#E6E1E5");
    public static readonly Color Muted = Color.FromArgb("#938F99");
    public static readonly Color Accent = Color.FromArgb("#D0BCFF");
    public static readonly Color Green = Color.FromArgb("#2EE6A6");
    public static readonly Color Danger = Color.FromArgb("#FF6B6B");
    public static readonly Color Surface = Color.FromArgb("#1C2B42");
    public static readonly Color StatusError = Color.FromArgb("#B3261E");
    public static readonly Color Hold = Color.FromArgb("#2EE6A6");
    public static readonly Color OnPrimary = Color.FromArgb("#EADDFF");
}

static class DemoUi
{
    /// <summary>Same banner metrics as Flutter / RN FacePluginLogo (size=120 → 264×52.8).</summary>
    public static Image Logo(double size = 120) => new()
    {
        Source = "fp_logo.png",
        WidthRequest = size * 2.2,
        HeightRequest = size * 0.44,
        Aspect = Aspect.AspectFit,
        HorizontalOptions = LayoutOptions.Center,
    };

    // Paths match FaceRecognition RN TileIcons / Android drawables.
    const string EnrollPath =
        "M15,12c2.21,0 4,-1.79 4,-4s-1.79,-4 -4,-4 -4,1.79 -4,4 1.79,4 4,4zM6,10V7H4v3H1v2h3v3h2v-3h3v-2H6zM15,14c-2.67,0 -8,1.34 -8,4v2h16v-2c0,-2.66 -5.33,-4 -8,-4z";
    const string IdentifyPersonPath =
        "M12,12c2.21,0 4,-1.79 4,-4s-1.79,-4 -4,-4 -4,1.79 -4,4 1.79,4 4,4zM12,14c-2.67,0 -8,1.34 -8,4v2h16v-2c0,-2.66 -5.33,-4 -8,-4z";
    const string IdentifyTicksPath =
        "M19,3h-2v2h2V3zM19,7h-2v2h2V7zM19,11h-2v2h2V11z";
    const string SettingsPath =
        "M19.14,12.94c0.04,-0.31 0.06,-0.63 0.06,-0.94c0,-0.31 -0.02,-0.63 -0.06,-0.94l2.03,-1.58c0.18,-0.14 0.23,-0.41 0.12,-0.61l-1.92,-3.32c-0.12,-0.22 -0.37,-0.29 -0.59,-0.22l-2.39,0.96c-0.5,-0.38 -1.03,-0.7 -1.62,-0.94L14.4,2.81c-0.04,-0.24 -0.24,-0.41 -0.48,-0.41h-3.84c-0.24,0 -0.43,0.17 -0.47,0.41L9.25,5.35C8.66,5.59 8.12,5.92 7.63,6.29L5.24,5.33c-0.22,-0.08 -0.47,0 -0.59,0.22L2.74,8.87C2.62,9.08 2.66,9.34 2.86,9.48l2.03,1.58C4.84,11.37 4.8,11.69 4.8,12s0.02,0.63 0.06,0.94l-2.03,1.58c-0.18,0.14 -0.23,0.41 -0.12,0.61l1.92,3.32c0.12,0.22 0.37,0.29 0.59,0.22l2.39,-0.96c0.5,0.38 1.03,0.7 1.62,0.94l0.36,2.54c0.05,0.24 0.24,0.41 0.48,0.41h3.84c0.24,0 0.44,-0.17 0.47,-0.41l0.36,-2.54c0.59,-0.24 1.13,-0.56 1.62,-0.94l2.39,0.96c0.22,0.08 0.47,0 0.59,-0.22l1.92,-3.32c0.12,-0.22 0.07,-0.47 -0.12,-0.61L19.14,12.94zM12,15.6c-1.98,0 -3.6,-1.62 -3.6,-3.6s1.62,-3.6 3.6,-3.6s3.6,1.62 3.6,3.6S13.98,15.6 12,15.6z";
    const string AboutPath =
        "M12,2C6.48,2 2,6.48 2,12s4.48,10 10,10 10,-4.48 10,-10S17.52,2 12,2zM13,17h-2v-6h2v6zM13,9h-2V7h2v2z";

    public static Border TileButton(string title, string kind, Action onClick, bool enabled = true)
    {
        var stack = new VerticalStackLayout
        {
            Padding = new Thickness(4, 14),
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            Children =
            {
                CreateTileIcon(kind),
                new Label
                {
                    Text = title,
                    TextColor = DemoTheme.Text,
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1,
                    InputTransparent = true,
                },
            },
        };
        var border = new Border
        {
            BackgroundColor = DemoTheme.Tile,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            HeightRequest = 120,
            Content = stack,
            Opacity = enabled ? 1 : 0.45,
        };
        if (enabled)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onClick();
            border.GestureRecognizers.Add(tap);
        }

        return border;
    }

    public static View BackButton(Action onClick) => Icons.Circle("back", onClick);

    static View CreateTileIcon(string kind, double size = 40)
    {
        var grid = new Grid
        {
            WidthRequest = size,
            HeightRequest = size,
            HorizontalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };

        if (kind is "capture" or "liveness")
        {
            grid.Add(new Ellipse
            {
                Stroke = DemoTheme.Text,
                StrokeThickness = 2,
                Fill = Colors.Transparent,
                WidthRequest = size * 0.72,
                HeightRequest = size * 0.72,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            });
            grid.Add(new Ellipse
            {
                Fill = DemoTheme.Text,
                WidthRequest = size * 0.28,
                HeightRequest = size * 0.28,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            });
            return grid;
        }

        if (kind == "attribute")
        {
            // Smiley — Android ic_attribute / RN TileIcons.
            grid.Add(new Ellipse
            {
                Stroke = DemoTheme.Text,
                StrokeThickness = 2,
                Fill = Colors.Transparent,
                WidthRequest = size * 0.85,
                HeightRequest = size * 0.85,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            });
            grid.Add(new Ellipse
            {
                Fill = DemoTheme.Text,
                WidthRequest = 5,
                HeightRequest = 5,
                Margin = new Thickness(-10, -4, 0, 0),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            });
            grid.Add(new Ellipse
            {
                Fill = DemoTheme.Text,
                WidthRequest = 5,
                HeightRequest = 5,
                Margin = new Thickness(10, -4, 0, 0),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            });
            grid.Add(new Microsoft.Maui.Controls.Shapes.Path
            {
                Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(
                    "M8.5,15.5 C10,17.2 11.2,18 12.5,18 C13.8,18 15,17.2 16.5,15.5")!,
                Stroke = DemoTheme.Text,
                StrokeThickness = 2,
                Aspect = Stretch.Uniform,
                WidthRequest = size * 0.7,
                HeightRequest = size * 0.35,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true,
            });
            return grid;
        }

        if (kind == "identify")
        {
            grid.Add(PathGlyph(IdentifyPersonPath, size));
            grid.Add(PathGlyph(IdentifyTicksPath, size));
            return grid;
        }

        var pathData = kind switch
        {
            "enroll" => EnrollPath,
            "settings" => SettingsPath,
            _ => AboutPath,
        };
        grid.Add(PathGlyph(pathData, size));
        return grid;
    }

    static Microsoft.Maui.Controls.Shapes.Path PathGlyph(string pathData, double size) => new()
    {
        Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(pathData)!,
        Fill = DemoTheme.Text,
        Aspect = Stretch.Uniform,
        WidthRequest = size,
        HeightRequest = size,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center,
        InputTransparent = true,
    };
}
