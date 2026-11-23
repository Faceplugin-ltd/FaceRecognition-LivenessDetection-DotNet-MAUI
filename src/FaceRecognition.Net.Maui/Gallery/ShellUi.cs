namespace FaceRecognition.Net.Maui;

/// <summary>
/// The shared look of the demo shell: the status colours, the formatting rules and the small
/// views every screen is built from. Mirrors the Android app's <c>Ui.kt</c> and
/// <c>view_section.xml</c>, so the same screen reads the same on all platforms.
/// </summary>
public static class ShellUi
{
    public static readonly Color StatusMatch = Color.FromArgb("#2EE6A6");
    public static readonly Color StatusNoMatch = Color.FromArgb("#E0A000");
    public static readonly Color StatusInfo = Color.FromArgb("#7FA0FF");
    public static readonly Color Danger = Color.FromArgb("#FF6B6B");
    public static readonly Color Card = Color.FromArgb("#2A2733");
    public static readonly Color Outline = Color.FromArgb("#49454F");

    /// <summary>
    /// The one title row. The shell bar is hidden, so this is the page name, with the back
    /// control and the page's own actions on the same row.
    /// </summary>
    public static Grid Header(string title, View? leading = null, params View[] trailing)
    {
        var grid = new Grid
        {
            ColumnSpacing = 4,
            MinimumHeightRequest = 48,
        };
        var column = 0;
        if (leading != null)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            leading.VerticalOptions = LayoutOptions.Center;
            grid.Add(leading, column++);
        }

        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Add(new Label
        {
            Text = title,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            TextColor = DemoTheme.Text,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        }, column++);

        foreach (var item in trailing)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            item.VerticalOptions = LayoutOptions.Center;
            grid.Add(item, column++);
        }

        return grid;
    }

    public static View Back(Page page, Func<Task>? go = null) =>
        Icons.Button("back", DemoTheme.Text, () => _ = go != null ? go() : GoBackAsync(page));

    public static async Task GoBackAsync(Page page)
    {
        if (page.Navigation.ModalStack.Contains(page))
        {
            await page.Navigation.PopModalAsync();
            return;
        }

        if (page.Navigation.NavigationStack.Count > 1)
        {
            await page.Navigation.PopAsync();
        }
    }

    /// <summary>IDs are always shown as a tag "ID 12" — never "#12".</summary>
    public static string IdTag(int id) => $"ID {id}";

    public static string Score(double v) => double.IsFinite(v) ? v.ToString("F2") : "–";

    public static string Angle(double v) => double.IsFinite(v) ? v.ToString("F1") : "–";

    /// <summary>Under 100 ms keeps one decimal; above it the decimal is noise.</summary>
    public static string Ms(double v) =>
        !double.IsFinite(v) ? "–" : v >= 100 ? v.ToString("F0") : v.ToString("F1");

    public static string FacesCount(int n) => n == 1 ? "1 face" : $"{n} faces";

    public static string MatchesCount(int n) => n == 1 ? "1 match" : $"{n} matches";

    public static string EnrolledDate(string iso) =>
        DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToLocalTime().ToString("d MMM yyyy")
            : iso;

    public static string EnrolledDateTime(string iso) =>
        DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToLocalTime().ToString("d MMM yyyy, HH:mm")
            : iso;

    // ---- views ----

    /// <summary>A rounded, tinted label — outcomes, ID tags and badges.</summary>
    public static Border Pill(string text, Color? color = null, double fontSize = 12) => new()
    {
        BackgroundColor = color ?? Outline,
        Stroke = Colors.Transparent,
        Padding = new Thickness(10, 3),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 999 },
        Content = new Label
        {
            Text = text,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = color == null ? DemoTheme.Text : Colors.Black,
        },
    };

    /// <summary>The card every section and status block sits in.</summary>
    public static Border CardView(View content) => new()
    {
        BackgroundColor = Card,
        Stroke = Outline,
        StrokeThickness = 1,
        Padding = 16,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
        Content = content,
    };

    /// <summary>One big number with a caption under it. Used in rows of three.</summary>
    public static View MetricTile(string value, string label) => new Border
    {
        BackgroundColor = Color.FromArgb("#332F3D"),
        Stroke = Colors.Transparent,
        Padding = new Thickness(8, 12),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
        Content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label
                {
                    Text = value,
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = DemoTheme.Text,
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.TailTruncation,
                },
                new Label
                {
                    Text = label,
                    FontSize = 11,
                    TextColor = DemoTheme.Muted,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
            },
        },
    };

    /// <summary>Three metric tiles sharing the width equally.</summary>
    public static View MetricsRow(params View[] tiles)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < tiles.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            grid.Add(tiles[i], i);
        }
        return grid;
    }

    /// <summary>A thin progress bar; <paramref name="value"/> is 0..1.</summary>
    public static View Bar(double value, Color? color = null) => new ProgressBar
    {
        Progress = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1),
        ProgressColor = color ?? DemoTheme.Accent,
        HeightRequest = 4,
    };

    /// <summary>A titled card holding label/value rows.</summary>
    public static View Section(string title, params View[] rows)
    {
        var column = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = DemoTheme.Text,
                    Margin = new Thickness(0, 0, 0, 4),
                },
            },
        };
        foreach (var row in rows)
        {
            column.Children.Add(row);
        }
        return CardView(column);
    }

    public enum Badge { Pass, Fail, Warn, Info }

    public static Color BadgeColor(Badge badge) => badge switch
    {
        Badge.Pass => StatusMatch,
        Badge.Fail => Danger,
        Badge.Warn => StatusNoMatch,
        _ => StatusInfo,
    };

    /// <summary>
    /// One label / value row, with an optional badge and a score bar under it.
    /// </summary>
    public static View Row(
        string label,
        string value,
        (string Text, Badge Kind)? badge = null,
        double? score = null,
        Action? onTap = null)
    {
        var line = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(108)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        line.Add(new Label
        {
            Text = label,
            FontSize = 13,
            TextColor = DemoTheme.Muted,
            VerticalTextAlignment = TextAlignment.Start,
        }, 0);
        line.Add(new Label
        {
            Text = value,
            FontSize = 14,
            TextColor = DemoTheme.Text,
        }, 1);
        if (badge is { } b)
        {
            line.Add(Pill(b.Text, BadgeColor(b.Kind), 11), 2);
        }

        var column = new VerticalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(0, 8),
            Children = { line },
        };
        if (score is { } s && double.IsFinite(s))
        {
            column.Children.Add(Bar(s, badge is { } bb ? BadgeColor(bb.Kind) : null));
        }

        if (onTap != null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => onTap();
            column.GestureRecognizers.Add(tap);
        }
        return column;
    }

    /// <summary>
    /// One status UI for every action: outcome pill, total time, when it finished, then one
    /// row per SDK call with pass/fail, its time and its share of the total.
    /// </summary>
    public static View StatusCard(CallReport report, string? title = null, bool showDetail = true)
    {
        var ok = report.Steps.Count(s => s.Ok);
        var at = report.FinishedAtLocal?.ToString("HH:mm:ss") ?? "";
        var summary = string.Join(
            "  ·  ",
            new[] { $"Total {Ms(report.TotalMs)} ms", $"{ok} of {report.Steps.Count} calls" }
                .Concat(at.Length > 0 ? new[] { at } : Array.Empty<string>()));

        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        header.Add(new Label
        {
            Text = title ?? $"{FunctionName(report.Function)} status",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = DemoTheme.Text,
        }, 0);
        header.Add(Pill(report.Success ? "Success" : "Failed", report.Success ? StatusMatch : Danger), 1);

        var column = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                header,
                new Label { Text = summary, FontSize = 12, TextColor = DemoTheme.Muted },
            },
        };

        var total = report.TotalMs > 0 ? report.TotalMs : 1;
        if (report.Steps.Count == 0)
        {
            column.Children.Add(new Label
            {
                Text = "No SDK calls were made.",
                FontSize = 13,
                TextColor = DemoTheme.Muted,
            });
        }
        else
        {
            foreach (var step in report.Steps)
            {
                column.Children.Add(StepRow(step, total));
            }
        }

        if (showDetail && !string.IsNullOrWhiteSpace(report.Detail))
        {
            column.Children.Add(new Label
            {
                Text = report.Detail,
                FontSize = 13,
                TextColor = DemoTheme.Text,
            });
        }
        return CardView(column);
    }

    /// <summary>Readable names for the stable step identifiers.</summary>
    public static string StepName(string name) => name switch
    {
        "faceDetection" => "Face detection",
        "templateExtraction" => "Template extraction",
        "similarity" => "1:N search",
        "livenessCheck" => "Liveness check",
        "duplicateCheck" => "Duplicate check (1:N)",
        "save" => "Save to gallery",
        _ => name,
    };

    public static string FunctionName(string fn) => fn switch
    {
        "identify" => "Identify",
        "enroll" => "Enroll",
        "attribute" => "Attribute",
        _ => string.IsNullOrEmpty(fn) ? fn : char.ToUpperInvariant(fn[0]) + fn[1..],
    };

    private static View StepRow(CallStep step, double total)
    {
        var color = step.Ok ? StatusMatch : Danger;
        var share = Math.Clamp(step.Ms / total, step.Ms > 0 ? 0.01 : 0, 1);

        var line = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        line.Add(new Label
        {
            Text = StepName(step.Name),
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = DemoTheme.Text,
        }, 0);
        line.Add(new Label
        {
            Text = $"{Ms(step.Ms)} ms",
            FontSize = 12,
            TextColor = DemoTheme.Muted,
        }, 1);

        var body = new VerticalStackLayout { Spacing = 2, Children = { line } };
        if (step.Detail.Length > 0)
        {
            body.Children.Add(new Label
            {
                Text = step.Detail,
                FontSize = 12,
                TextColor = DemoTheme.Muted,
            });
        }
        body.Children.Add(Bar(share, color));

        var row = new Grid
        {
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, 10),
            ColumnDefinitions = { new ColumnDefinition(new GridLength(18)), new ColumnDefinition(GridLength.Star) },
        };
        row.Add(new Border
        {
            BackgroundColor = color,
            Stroke = Colors.Transparent,
            WidthRequest = 18,
            HeightRequest = 18,
            VerticalOptions = LayoutOptions.Start,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 },
            Content = new Label
            {
                Text = step.Ok ? "✓" : "✕",
                FontSize = 11,
                TextColor = Colors.Black,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        }, 0);
        row.Add(body, 1);
        return row;
    }
}
