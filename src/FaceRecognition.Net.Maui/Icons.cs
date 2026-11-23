using Microsoft.Maui.Controls.Shapes;

namespace FaceRecognition.Net.Maui;

/// <summary>
/// The same 24×24 Material glyphs the Android drawables and the iOS tab bar use.
/// </summary>
static class Icons
{
    const string Identity =
        "M9,11.75c-0.69,0 -1.25,0.56 -1.25,1.25s0.56,1.25 1.25,1.25 1.25,-0.56 1.25,-1.25 -0.56,-1.25 -1.25,-1.25zM15,11.75c-0.69,0 -1.25,0.56 -1.25,1.25s0.56,1.25 1.25,1.25 1.25,-0.56 1.25,-1.25 -0.56,-1.25 -1.25,-1.25zM12,2C6.48,2 2,6.48 2,12s4.48,10 10,10 10,-4.48 10,-10S17.52,2 12,2zM12,20c-4.41,0 -8,-3.59 -8,-8 0,-0.29 0.02,-0.58 0.05,-0.86 2.36,-1.05 4.23,-2.98 5.21,-5.37C11.07,8.33 14.05,10 17.42,10c0.78,0 1.53,-0.09 2.25,-0.26 0.21,0.71 0.33,1.47 0.33,2.26 0,4.41 -3.59,8 -8,8z";
    const string People =
        "M16,11c1.66,0 2.99,-1.34 2.99,-3S17.66,5 16,5c-1.66,0 -3,1.34 -3,3s1.34,3 3,3zM8,11c1.66,0 2.99,-1.34 2.99,-3S9.66,5 8,5C6.34,5 5,6.34 5,8s1.34,3 3,3zM8,13c-2.33,0 -7,1.17 -7,3.5V19h14v-2.5c0,-2.33 -4.67,-3.5 -7,-3.5zM16,13c-0.29,0 -0.62,0.02 -0.97,0.05 1.16,0.84 1.97,1.97 1.97,3.45V19h6v-2.5c0,-2.33 -4.67,-3.5 -7,-3.5z";
    const string Attribute =
        "M9,11.75c0.69,0 1.25,-0.56 1.25,-1.25S9.69,9.25 9,9.25 7.75,9.81 7.75,10.5 8.31,11.75 9,11.75zM15,11.75c0.69,0 1.25,-0.56 1.25,-1.25s-0.56,-1.25 -1.25,-1.25 -1.25,0.56 -1.25,1.25 0.56,1.25 1.25,1.25zM12,2C6.48,2 2,6.48 2,12s4.48,10 10,10 10,-4.48 10,-10S17.52,2 12,2zM12,20c-4.41,0 -8,-3.59 -8,-8 0,-0.29 0.02,-0.58 0.05,-0.86 2.36,-1.05 4.23,-2.98 5.21,-5.37C11.07,8.33 14.05,10 17.42,10c0.78,0 1.53,-0.09 2.25,-0.26 0.21,0.71 0.33,1.47 0.33,2.26 0,4.41 -3.59,8 -8,8z";
    const string Liveness =
        "M12,4.5C7,4.5 2.73,7.61 1,12c1.73,4.39 6,7.5 11,7.5s9.27,-3.11 11,-7.5c-1.73,-4.39 -6,-7.5 -11,-7.5zM12,17a5,5 0 1 1 0,-10 5,5 0 0 1 0,10zM12,9a3,3 0 1 0 0,6 3,3 0 0 0 0,-6z";
    const string Settings =
        "M19.14,12.94c0.04,-0.31 0.06,-0.63 0.06,-0.94c0,-0.31 -0.02,-0.63 -0.06,-0.94l2.03,-1.58c0.18,-0.14 0.23,-0.41 0.12,-0.61l-1.92,-3.32c-0.12,-0.22 -0.37,-0.29 -0.59,-0.22l-2.39,0.96c-0.5,-0.38 -1.03,-0.7 -1.62,-0.94L14.4,2.81c-0.04,-0.24 -0.24,-0.41 -0.48,-0.41h-3.84c-0.24,0 -0.43,0.17 -0.47,0.41L9.25,5.35C8.66,5.59 8.12,5.92 7.63,6.29L5.24,5.33c-0.22,-0.08 -0.47,0 -0.59,0.22L2.74,8.87C2.62,9.08 2.66,9.34 2.86,9.48l2.03,1.58C4.84,11.37 4.82,11.69 4.82,12s0.02,0.63 0.06,0.94l-2.03,1.58c-0.18,0.14 -0.23,0.41 -0.12,0.61l1.92,3.32c0.12,0.22 0.37,0.29 0.59,0.22l2.39,-0.96c0.5,0.38 1.03,0.7 1.62,0.94l0.36,2.54c0.05,0.24 0.24,0.41 0.48,0.41h3.84c0.24,0 0.44,-0.17 0.47,-0.41l0.36,-2.54c0.59,-0.24 1.13,-0.56 1.62,-0.94l2.39,0.96c0.22,0.08 0.47,0 0.59,-0.22l1.92,-3.32c0.12,-0.22 0.07,-0.47 -0.12,-0.61L19.14,12.94zM12,15.6c-1.98,0 -3.6,-1.62 -3.6,-3.6s1.62,-3.6 3.6,-3.6s3.6,1.62 3.6,3.6S13.98,15.6 12,15.6z";
    const string PersonAdd =
        "M15,12c2.21,0 4,-1.79 4,-4s-1.79,-4 -4,-4 -4,1.79 -4,4 1.79,4 4,4zM6,10V7H4v3H1v2h3v3h2v-3h3v-2H6zM15,14c-2.67,0 -8,1.34 -8,4v2h16v-2c0,-2.66 -5.33,-4 -8,-4z";
    const string Delete =
        "M6,19c0,1.1 0.9,2 2,2h8c1.1,0 2,-0.9 2,-2V7H6v12zM19,4h-3.5l-1,-1h-5l-1,1H5v2h14V4z";
    const string Gallery =
        "M22,16V4c0,-1.1 -0.9,-2 -2,-2H8c-1.1,0 -2,0.9 -2,2v12c0,1.1 0.9,2 2,2h12c1.1,0 2,-0.9 2,-2zM11.5,9.5l2.02,2.71L16.5,9l3.5,5H8l3.5,-4.5zM2,6v14c0,1.1 0.9,2 2,2h14v-2H4V6H2z";
    const string SwitchCamera =
        "M16,7h-1l-1,-1h-4L9,7H8c-1.1,0 -2,0.9 -2,2v6c0,1.1 0.9,2 2,2h8c1.1,0 2,-0.9 2,-2V9c0,-1.1 -0.9,-2 -2,-2zM12,14c-1.1,0 -2,-0.9 -2,-2s0.9,-2 2,-2 2,0.9 2,2 -0.9,2 -2,2zM8.57,0.51l4.48,4.48V2.04c4.72,0.47 8.48,4.23 8.95,8.95h2C23.34,4.02 16.66,-1.53 8.57,0.51zM10.95,21.96c-4.72,-0.47 -8.48,-4.23 -8.95,-8.95H0c0.66,6.97 7.34,12.52 15.43,10.48l-4.48,-4.48v2.95z";
    const string Reset =
        "M12,5V2L8,6l4,4V7c3.31,0 6,2.69 6,6c0,2.97 -2.17,5.43 -5,5.91v2.02c3.95,-0.49 7,-3.85 7,-7.93C20,8.58 16.42,5 12,5zM6,13c0,-1.65 0.67,-3.15 1.76,-4.24L6.34,7.34C4.9,8.79 4,10.79 4,13c0,4.08 3.05,7.44 7,7.93v-2.02C8.17,18.43 6,15.97 6,13z";
    const string Back =
        "M20,11H7.83l5.59,-5.59L12,4l-8,8 8,8 1.41,-1.41L7.83,13H20v-2z";
    const string Chevron =
        "M10,6L8.59,7.41 13.17,12l-4.58,4.59L10,18l6,-6z";
    const string Close =
        "M19,6.41L17.59,5 12,10.59 6.41,5 5,6.41 10.59,12 5,17.59 6.41,19 12,13.41 17.59,19 19,17.59 13.41,12z";

    public static View Glyph(string name, Color color, double size = 24)
    {
        var data = name switch
        {
            "identity" => Identity,
            "people" => People,
            "attribute" => Attribute,
            "liveness" => Liveness,
            "settings" => Settings,
            "personAdd" => PersonAdd,
            "delete" => Delete,
            "gallery" => Gallery,
            "switchCamera" => SwitchCamera,
            "reset" => Reset,
            "back" => Back,
            "chevron" => Chevron,
            "close" => Close,
            _ => People,
        };
        return new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(data)!,
            Fill = color,
            Aspect = Stretch.Uniform,
            WidthRequest = size,
            HeightRequest = size,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };
    }

    /// <summary>A 48dp header control, the same size as the Android image buttons.</summary>
    public static View Button(string name, Color color, Action onClick)
    {
        var hit = new Grid
        {
            WidthRequest = 48,
            HeightRequest = 48,
            Children = { Glyph(name, color, 24) },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onClick();
        hit.GestureRecognizers.Add(tap);
        return hit;
    }

    /// <summary>Round camera control (flip) sitting on the preview.</summary>
    public static View Circle(string name, Action onClick)
    {
        var hit = new Border
        {
            BackgroundColor = Color.FromArgb("#66000000"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            WidthRequest = 44,
            HeightRequest = 44,
            Content = Glyph(name, Colors.White, 22),
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onClick();
        hit.GestureRecognizers.Add(tap);
        return hit;
    }
}
