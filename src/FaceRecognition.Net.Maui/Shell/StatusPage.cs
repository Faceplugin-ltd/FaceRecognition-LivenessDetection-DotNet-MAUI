namespace FaceRecognition.Net.Maui;

/// <summary>
/// The enrolment / identify status, shown as a full-height sheet rather than an alert: it is a
/// report of what the engine did, and the steps should not open inside a box the reader has to
/// grow by scrolling. The same surface the capture review uses, so one replaces the other.
/// </summary>
public class StatusPage : ContentPage
{
    public StatusPage(string title, string? reason, CallReport? report)
    {
        Title = title;
        BackgroundColor = DemoTheme.Bg;

        var done = new Button
        {
            Text = "Done",
            TextColor = DemoTheme.Accent,
            BackgroundColor = Colors.Transparent,
        };
        done.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var head = ShellUi.Header(title, ShellUi.Back(this), done);
        head.Padding = new Thickness(4, 4, 8, 0);

        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 8, 16, 16),
        };

        if (!string.IsNullOrWhiteSpace(reason))
        {
            body.Children.Add(new Label
            {
                Text = reason,
                FontSize = 14,
                TextColor = DemoTheme.Muted,
            });
        }

        if (report != null)
        {
            body.Children.Add(ShellUi.StatusCard(report));
        }

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
}
