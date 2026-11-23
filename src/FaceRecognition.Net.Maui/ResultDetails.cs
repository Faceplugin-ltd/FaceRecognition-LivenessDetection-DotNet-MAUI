namespace FaceRecognition.Net.Maui;

public readonly record struct DetailRow(string Kind, string Title, string Value);

/// <summary>Mirrors RN/Flutter resultDetailRows (Android ResultDetails).</summary>
public static class ResultDetails
{
    public static bool LivenessPassed(AppSettings settings, double score, string? label)
    {
        var lower = (label ?? "").ToLowerInvariant();
        if (lower.Contains("spoof") || lower.Contains("fake"))
        {
            return false;
        }

        // Flutter/RN livenessPassed: strong positive labels pass even at ~0 score.
        if (lower.Contains("real")
            || lower.Contains("genuine")
            || (lower.Contains("live") && !lower.Contains("liveness")))
        {
            if (score <= 0.001)
            {
                return true;
            }
        }

        return score >= settings.LivenessThreshold;
    }

    public static string QualityText(double score)
    {
        if (score < 0.5)
        {
            return "Low · " + Math.Round(score * 100) + "%";
        }

        if (score < 0.75)
        {
            return "Medium · " + Math.Round(score * 100) + "%";
        }

        return "High · " + Math.Round(score * 100) + "%";
    }

    public static string LivenessText(AppSettings settings, double score, string? label)
    {
        var lower = (label ?? "").ToLowerInvariant();
        string live;
        if (lower.Contains("spoof") || lower.Contains("fake"))
        {
            live = "Spoof";
        }
        else if (lower.Contains("real"))
        {
            live = "Real";
        }
        else
        {
            live = LivenessPassed(settings, score, label) ? "Real" : "Spoof";
        }

        if (label != null && label.Contains(" · "))
        {
            return label;
        }

        return live + " · " + Math.Round(score * 100) + "%";
    }

    public static IReadOnlyList<DetailRow> Rows(
        FaceBox box,
        AppSettings settings,
        string? personName = null,
        double? similarity = null,
        bool includeMatch = false)
    {
        var rows = new List<DetailRow>();
        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in box.Attributes)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                extra[pair.Key] = pair.Value;
            }
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Push(string title, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(new DetailRow("field", title, value));
            }
        }

        void Section(string title) => rows.Add(new DetailRow("section", title, title));

        string Engine(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (extra.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                {
                    return v;
                }
            }

            return "";
        }

        void Take(string title, string[] keys, string? fallback = null)
        {
            var value = Engine(keys) is { Length: > 0 } e ? e : (fallback ?? "");
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            Push(title, value);
            foreach (var key in keys)
            {
                used.Add(key);
            }
        }

        if (includeMatch && !string.IsNullOrWhiteSpace(personName))
        {
            Section("Match");
            Push("Person", personName!);
            if (similarity != null)
            {
                Push("Similarity", Math.Round(similarity.Value * 100) + "%");
            }
        }

        Section("Authenticity");
        Take(
            "Liveness",
            ["Liveness2D", "liveness", "Liveness"],
            LivenessText(settings, box.Liveness ?? 0, box.LivenessLabel));

        Section("Person");
        Take("Age", ["Age", "age"], box.Age is > 0 ? box.Age.Value.ToString("0") : null);
        Take("Gender", ["Gender", "gender"], box.GenderLabel);
        Take("Emotion", ["Emotion", "emotion"], box.EmotionLabel);
        Take("All emotions", ["Emotions"]);

        Section("Face");
        Take("Mask", ["MedicalMask", "Mask", "mask"], box.MaskLabel);
        Take("Glasses", ["Glasses", "glasses"], box.GlassesLabel);
        Take("Sunglasses", ["Sunglasses", "sunglasses"], box.SunglassesLabel);
        Take("Occlusion", ["Occlusion", "FaceOcclusion", "occlusion"], box.OcclusionLabel);
        var left = Engine("EyesLeft", "eyesLeft");
        if (string.IsNullOrWhiteSpace(left))
        {
            left = box.EyesLeftLabel ?? "";
        }

        var right = Engine("EyesRight", "eyesRight");
        if (string.IsNullOrWhiteSpace(right))
        {
            right = box.EyesRightLabel ?? "";
        }

        if (!string.IsNullOrWhiteSpace(left) || !string.IsNullOrWhiteSpace(right))
        {
            Push("Eyes", "Left  " + (string.IsNullOrWhiteSpace(left) ? "—" : left)
                + "\nRight  " + (string.IsNullOrWhiteSpace(right) ? "—" : right));
            used.Add("EyesLeft");
            used.Add("EyesRight");
        }

        Section("Quality");
        Take(
            "Overall",
            ["FaceQuality", "ExpressionLevel", "face_quality"],
            string.IsNullOrWhiteSpace(box.QualityLabel)
                ? QualityText(box.FaceQuality ?? 0)
                : box.QualityLabel);
        foreach (var key in new[] { "Lighting", "Sharpness", "Noise", "Flare", "BlurLevel", "NoiseLevel" })
        {
            Take(key, [key]);
        }

        Section("Geometry");
        Push("Pose", $"yaw {box.Yaw ?? 0}°   roll {box.Roll ?? 0}°   pitch {box.Pitch ?? 0}°");
        Push("Box", $"{box.X1}, {box.Y1} → {box.X2}, {box.Y2}");
        if ((box.FaceLuminance ?? 0) > 0)
        {
            Push("Luminance", Math.Round((box.FaceLuminance ?? 0) * 100) + "%");
        }

        var leftovers = extra
            .Where(p => !string.IsNullOrWhiteSpace(p.Value)
                && !used.Contains(p.Key)
                && !p.Key.Equals("MouthOpened", StringComparison.OrdinalIgnoreCase)
                && !p.Key.Equals("Deepfake", StringComparison.OrdinalIgnoreCase)
                && !p.Key.Equals("Template", StringComparison.OrdinalIgnoreCase)
                && !rows.Any(r => r.Kind == "field" && r.Title.Equals(p.Key, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (leftovers.Count > 0)
        {
            Section("More from engine");
            foreach (var pair in leftovers)
            {
                Push(pair.Key, pair.Value);
            }
        }

        if (box.Landmarks.Count >= 2)
        {
            Section("Landmarks");
            var n = box.Landmarks.Count / 2;
            Push("Count", n + " points");
            var lines = new List<string>();
            for (var i = 0; i < n; i++)
            {
                lines.Add($"{i + 1}: {box.Landmarks[i * 2]}, {box.Landmarks[i * 2 + 1]}");
            }

            Push("Positions", string.Join("\n", lines));
        }

        return rows.Where((row, i) =>
        {
            if (row.Kind != "section")
            {
                return !string.IsNullOrWhiteSpace(row.Value);
            }

            return i + 1 < rows.Count && rows[i + 1].Kind == "field" && !string.IsNullOrWhiteSpace(rows[i + 1].Value);
        }).ToList();
    }

    public static View BuildList(IEnumerable<DetailRow> rows)
    {
        var stack = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 0, 0, 32) };
        foreach (var row in rows)
        {
            if (row.Kind == "section")
            {
                stack.Add(new Label
                {
                    Text = row.Title.ToUpperInvariant(),
                    TextColor = DemoTheme.Accent,
                    FontSize = 13,
                    CharacterSpacing = 1,
                    Margin = new Thickness(16, 20, 16, 4),
                });
            }
            else
            {
                stack.Add(new VerticalStackLayout
                {
                    Padding = new Thickness(16, 10, 16, 0),
                    Spacing = 2,
                    Children =
                    {
                        new Label { Text = row.Title, TextColor = DemoTheme.Muted, FontSize = 13 },
                        new Label { Text = row.Value, TextColor = DemoTheme.Text, FontSize = 16 },
                    },
                });
            }
        }

        return stack;
    }
}
