using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaceRecognition.Net.Maui;

/// <summary>One enrolled face. The photos are JPEG files in app-private storage.</summary>
public sealed class FaceRecord
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("note")]
    public string Note { get; set; } = "";

    /// <summary>ISO-8601 UTC.</summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = "";

    /// <summary>Base64 recognition template.</summary>
    [JsonPropertyName("feature")]
    public string Feature { get; init; } = "";

    [JsonPropertyName("cropPath")]
    public string CropPath { get; set; } = "";

    [JsonPropertyName("originalPath")]
    public string OriginalPath { get; set; } = "";

    /// <summary><see cref="CallReport"/> JSON; empty for people enrolled before it was stored.</summary>
    [JsonPropertyName("enrollReport")]
    public string EnrollReport { get; set; } = "";

    [JsonIgnore]
    public CallReport? Report => CallReport.FromJson(EnrollReport);

    [JsonIgnore]
    public DateTime? CreatedAtLocal =>
        DateTime.TryParse(CreatedAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToLocalTime()
            : null;
}

/// <summary>One gallery hit for a probe face.</summary>
public sealed record Candidate(int Id, string Name, string Note, double Similarity);

public sealed class ProbeFace
{
    public int Index { get; init; }
    public FaceBox Box { get; init; } = null!;

    /// <summary>Top <see cref="FaceGallery.TopK"/> by similarity, highest first.</summary>
    public IReadOnlyList<Candidate> Ranked { get; init; } = Array.Empty<Candidate>();

    public Candidate? Best => Ranked.Count > 0 ? Ranked[0] : null;
}

public sealed class IdentifyResult
{
    public byte[] ProbeJpeg { get; init; } = Array.Empty<byte>();
    public IReadOnlyList<ProbeFace> Faces { get; init; } = Array.Empty<ProbeFace>();
    public double Threshold { get; init; }
    public double ExtractMs { get; init; }
    public double SearchMs { get; init; }
    public int GallerySize { get; init; }
    public CallReport Report { get; init; } = null!;

    /// <summary>
    /// The face whose best candidate scores highest. Falls back to the first face when nothing
    /// scored, so a probe against an empty gallery still has something to show.
    /// </summary>
    public ProbeFace? Primary
    {
        get
        {
            if (Faces.Count == 0)
            {
                return null;
            }
            var best = Faces[0];
            var top = best.Best?.Similarity ?? -1;
            foreach (var f in Faces)
            {
                var s = f.Best?.Similarity ?? -1;
                if (s > top)
                {
                    top = s;
                    best = f;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Matches at or above the threshold across all probe faces, best score per person,
    /// highest first.
    /// </summary>
    public IReadOnlyList<Candidate> Matches
    {
        get
        {
            var best = new Dictionary<int, Candidate>();
            foreach (var face in Faces)
            {
                foreach (var c in face.Ranked)
                {
                    if (c.Similarity < Threshold)
                    {
                        continue;
                    }
                    if (!best.TryGetValue(c.Id, out var prev) || c.Similarity > prev.Similarity)
                    {
                        best[c.Id] = c;
                    }
                }
            }
            return best.Values.OrderByDescending(c => c.Similarity).ToArray();
        }
    }
}

/// <summary>One detected face on the Attribute page, with its best gallery hit.</summary>
public sealed class FaceAttributes
{
    public int Index { get; init; }
    public FaceBox Box { get; init; } = null!;
    public int TemplateBytes { get; init; }
    public Candidate? Closest { get; init; }
}

public sealed class AttributeResult
{
    public byte[] ProbeJpeg { get; init; } = Array.Empty<byte>();
    public IReadOnlyList<FaceAttributes> Faces { get; init; } = Array.Empty<FaceAttributes>();
    public double Threshold { get; init; }
    public int GallerySize { get; init; }
    public CallReport Report { get; init; } = null!;
}

public abstract record EnrollOutcome
{
    /// <summary>
    /// <paramref name="Duplicates"/> holds every already-enrolled person scoring at or above
    /// the identify threshold, highest first — not just the closest. One new face can match
    /// several existing ones, and hiding the rest makes the gallery look cleaner than it is.
    /// </summary>
    public sealed record Enrolled(
        FaceRecord Record,
        CallReport Report,
        IReadOnlyList<Candidate> Duplicates) : EnrollOutcome
    {
        /// <summary>The strongest match, for callers that only show one.</summary>
        public Candidate? Duplicate => Duplicates.Count > 0 ? Duplicates[0] : null;
    }

    public sealed record Rejected(string Reason, CallReport? Report) : EnrollOutcome;
}

public abstract record IdentifyOutcome
{
    public sealed record Done(IdentifyResult Result) : IdentifyOutcome;

    public sealed record Rejected(string Reason, CallReport? Report) : IdentifyOutcome;
}

/// <summary>
/// The on-device gallery: enroll, 1:N identify, attribute analysis, and the stored people.
///
/// Records live in a JSON file and the photos as JPEGs beside it, both in app-private
/// storage. Every engine call goes through <see cref="SdkGate"/>, so calls never overlap.
/// The steps, their details and the thresholds match the Android and iOS apps, because the
/// same status card has to read correctly on all of them.
/// </summary>
public static class FaceGallery
{
    public const int TopK = 100;
    public const int MaxAttributeFaces = 10;
    public const string NoFace = "No face found. Use a clear, front-facing photo.";
    public const string LivenessFailed =
        "Liveness check failed. Use a live face, not a photo or screen.";
    private const string ReadFail = "Could not read this face. Use a clear, front-facing photo.";

    /// <summary>Raised after any write, so open lists refresh without polling.</summary>
    public static event Action? Changed;

    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static List<FaceRecord>? _records;
    private static int _nextId = 1;

    private static string Dir =>
        Path.Combine(FileSystem.AppDataDirectory, "gallery");

    private static string StorePath => Path.Combine(Dir, "faces.json");

    private sealed class Store
    {
        [JsonPropertyName("nextId")]
        public int NextId { get; set; } = 1;

        [JsonPropertyName("records")]
        public List<FaceRecord> Records { get; set; } = new();
    }

    private static List<FaceRecord> Load()
    {
        if (_records != null)
        {
            return _records;
        }
        try
        {
            Directory.CreateDirectory(Dir);
            if (File.Exists(StorePath))
            {
                var store = JsonSerializer.Deserialize<Store>(File.ReadAllText(StorePath));
                _records = store?.Records ?? new List<FaceRecord>();
                _nextId = Math.Max(store?.NextId ?? 1, _records.Count == 0 ? 1 : _records.Max(r => r.Id) + 1);
            }
            else
            {
                _records = new List<FaceRecord>();
            }
        }
        catch
        {
            // A corrupt store starts empty rather than blocking the app.
            _records = new List<FaceRecord>();
        }
        return _records;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(
                StorePath,
                JsonSerializer.Serialize(new Store { NextId = _nextId, Records = Load() }));
        }
        catch
        {
            // Losing a write costs the user that person, not the session.
        }
        Changed?.Invoke();
    }

    // ---- read ----

    /// <summary>Newest first — ids increase and are never reused.</summary>
    public static IReadOnlyList<FaceRecord> Page(string query, int limit, int offset) =>
        Filtered(query).Skip(offset).Take(limit).ToArray();

    public static int Count(string query = "") => Filtered(query).Count();

    public static FaceRecord? Get(int id) => Load().FirstOrDefault(r => r.Id == id);

    private static IEnumerable<FaceRecord> Filtered(string query)
    {
        var q = (query ?? "").Trim();
        var all = Load().OrderByDescending(r => r.Id);
        if (q.Length == 0)
        {
            return all;
        }
        return all.Where(r =>
            r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            r.Note.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private static List<FaceRecord> Templates() =>
        Load().Where(r => r.Feature.Length > 0).ToList();

    // ---- edit / delete ----

    public static bool Update(int id, string name, string note)
    {
        var r = Get(id);
        if (r == null)
        {
            return false;
        }
        r.Name = name.Trim();
        r.Note = note.Trim();
        Save();
        return true;
    }

    public static bool Delete(int id)
    {
        var r = Get(id);
        if (r == null)
        {
            return false;
        }
        Load().Remove(r);
        DeleteFiles(r);
        Save();
        return true;
    }

    /// <summary>
    /// Removes every record and every stored photo, and restarts enroll ids at 1 — a freshly
    /// cleared demo gallery should not look like it still remembers the last one.
    /// Returns how many faces went.
    /// </summary>
    public static int DeleteAll()
    {
        var all = Load();
        var n = all.Count;
        foreach (var r in all)
        {
            DeleteFiles(r);
        }
        all.Clear();
        _nextId = 1;
        Save();
        return n;
    }

    private static void DeleteFiles(FaceRecord r)
    {
        foreach (var path in new[] { r.CropPath, r.OriginalPath })
        {
            try
            {
                if (path.Length > 0 && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A stranded JPEG is not worth failing the delete over.
            }
        }
    }

    // ---- enroll ----

    /// <summary>
    /// Exactly-one-face enrollment. Steps: detection, template, duplicate check (1:N against
    /// the gallery), save.
    /// </summary>
    public static async Task<EnrollOutcome> EnrollAsync(
        string name,
        string note,
        byte[] jpeg,
        FaceLimits limits)
    {
        var cleanName = name.Trim();
        if (!SdkSession.Ready)
        {
            return new EnrollOutcome.Rejected(SdkSession.Message, null);
        }

        var steps = new List<CallStep>();
        // One engine pass: the template comes back with the detection, from the same open
        // image, as the native kits do. The two still show as separate steps below.
        var faces = await Timed.RunAsync(
            steps,
            "faceDetection",
            async () => FaceJson.ParseDetect(await FaceSdk.DetectWithTemplatesAsync(
                jpeg, SettingsStore.Current.EnrollFlags, 1, false)),
            f => FacesDetail(f.Count),
            f => f.Count == 1).ConfigureAwait(false);
        SplitTemplateTime(steps, faces);

        if (faces.Count == 0)
        {
            return new EnrollOutcome.Rejected(
                NoFace, CallReport.Make("enroll", steps, "No face", "", false));
        }
        if (faces.Count > 1)
        {
            return new EnrollOutcome.Rejected(
                $"{faces.Count} faces found. Use a photo that shows only one person.",
                CallReport.Make("enroll", steps, $"{faces.Count} faces", "", false));
        }

        var face = faces[0];
        var feature = face.Template ?? "";
        steps.Add(new CallStep
        {
            Name = "templateExtraction",
            Ms = TemplateMs(faces),
            Detail = feature.Length == 0 ? "no template" : $"{Base64Bytes(feature)} bytes",
            Ok = feature.Length > 0,
        });

        if (feature.Length == 0)
        {
            return new EnrollOutcome.Rejected(
                ReadFail, CallReport.Make("enroll", steps, "template failed", "", false));
        }

        var gallery = Templates();
        var ranked = await Timed.RunAsync(
            steps,
            "duplicateCheck",
            () => RankAsync(feature, gallery),
            list =>
            {
                if (list.Count == 0)
                {
                    return "gallery empty";
                }
                var over = list.Where(c => c.Similarity >= limits.Match).ToArray();
                if (over.Length == 0)
                {
                    return $"closest {list[0].Name} · ID {list[0].Id} · {list[0].Similarity:F2}";
                }
                return string.Join(", ", over.Select(c => $"{c.Name} · ID {c.Id} · {c.Similarity:F2}")) +
                       " · possible duplicate" + (over.Length > 1 ? "s" : "");
            }).ConfigureAwait(false);

        var record = await Timed.RunAsync(
            steps,
            "save",
            async () =>
            {
                var id = _nextId++;
                Directory.CreateDirectory(Dir);
                var cropPath = Path.Combine(Dir, $"{id}_crop.jpg");
                var originalPath = Path.Combine(Dir, $"{id}_original.jpg");
                var crop = await FaceSdk.CropFaceAsync(jpeg, face).ConfigureAwait(false);
                if (crop is { Length: > 0 })
                {
                    await File.WriteAllBytesAsync(cropPath, crop).ConfigureAwait(false);
                }
                await File.WriteAllBytesAsync(originalPath, jpeg).ConfigureAwait(false);
                var savedName = cleanName.Length == 0 ? $"Person {id}" : cleanName;
                var saved = new FaceRecord
                {
                    Id = id,
                    Name = savedName,
                    Note = note.Trim(),
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    Feature = feature,
                    CropPath = File.Exists(cropPath) ? cropPath : "",
                    OriginalPath = originalPath,
                };
                Load().Add(saved);
                return saved;
            },
            r => $"ID {r.Id} · {gallery.Count + 1} enrolled").ConfigureAwait(false);

        var duplicates = ranked.Where(c => c.Similarity >= limits.Match).ToArray();
        var report = CallReport.Make(
            "enroll",
            steps,
            duplicates.Length == 0
                ? $"Saved {record.Name} as ID {record.Id}"
                : $"Saved {record.Name} as ID {record.Id} · looks like " +
                  string.Join(", ", duplicates.Select(c => $"{c.Name} (ID {c.Id})")),
            $"{steps.Count} of {steps.Count} · saved");
        record.EnrollReport = report.ToJson();
        Save();
        return new EnrollOutcome.Enrolled(record, report, duplicates);
    }

    // ---- identify ----

    public static async Task<IdentifyOutcome> IdentifyAsync(
        byte[] jpeg,
        FaceLimits limits,
        bool livenessOn)
    {
        if (!SdkSession.Ready)
        {
            return new IdentifyOutcome.Rejected(SdkSession.Message, null);
        }

        var steps = new List<CallStep>();
        var flags = SettingsStore.Current.IdentifyFlags(livenessOn);
        // One engine pass again: every probe face is templated from the same open image.
        var faces = await Timed.RunAsync(
            steps,
            "faceDetection",
            async () => FaceJson.ParseDetect(
                await FaceSdk.DetectWithTemplatesAsync(jpeg, flags, MaxAttributeFaces, false)),
            f => FacesDetail(f.Count),
            f => f.Count > 0).ConfigureAwait(false);
        SplitTemplateTime(steps, faces);

        if (faces.Count == 0)
        {
            return new IdentifyOutcome.Rejected(
                NoFace, CallReport.Make("identify", steps, "No face", "", false));
        }

        if (livenessOn)
        {
            var live = faces.Count(f => limits.IsLive(f.Liveness));
            var lowest = faces
                .Select(f => f.Liveness)
                .Where(v => v is { } d && double.IsFinite(d))
                .DefaultIfEmpty(double.NaN)
                .Min() ?? double.NaN;
            // Liveness is scored during detection; this step only applies the threshold.
            steps.Add(new CallStep
            {
                Name = "livenessCheck",
                Ms = 0,
                Detail = $"{live} of {faces.Count} live · lowest {lowest:F2} / {limits.Liveness:F2}",
                Ok = live == faces.Count,
            });
            if (live < faces.Count)
            {
                return new IdentifyOutcome.Rejected(
                    LivenessFailed,
                    CallReport.Make("identify", steps, "Liveness failed", "", false));
            }
        }

        var extracted = new List<(FaceBox Face, string Feature)>();
        foreach (var face in faces)
        {
            if (!string.IsNullOrEmpty(face.Template))
            {
                extracted.Add((face, face.Template!));
            }
        }
        steps.Add(new CallStep
        {
            Name = "templateExtraction",
            Ms = TemplateMs(faces),
            Detail = $"{extracted.Count} templates",
            Ok = extracted.Count > 0,
        });

        if (extracted.Count == 0)
        {
            return new IdentifyOutcome.Rejected(
                ReadFail, CallReport.Make("identify", steps, "template failed", "", false));
        }

        var gallery = Templates();
        var probeFaces = await Timed.RunAsync(
            steps,
            "similarity",
            async () =>
            {
                var list = new List<ProbeFace>();
                for (var i = 0; i < extracted.Count; i++)
                {
                    var ranked = await RankAsync(extracted[i].Feature, gallery).ConfigureAwait(false);
                    list.Add(new ProbeFace
                    {
                        Index = i,
                        Box = extracted[i].Face,
                        Ranked = ranked.Take(TopK).ToArray(),
                    });
                }
                return list;
            },
            _ => $"{gallery.Count} enrolled · {extracted.Count} probes").ConfigureAwait(false);

        double? best = null;
        foreach (var f in probeFaces)
        {
            var top = f.Best?.Similarity;
            if (top is { } t && (best is null || t > best))
            {
                best = t;
            }
        }

        var report = CallReport.Make(
            "identify",
            steps,
            best is null
                ? "Gallery empty"
                : $"Best similarity {best:F2} · threshold {limits.Match:F2}",
            $"{steps.Count} of {steps.Count} · similarity");
        var searchMs = steps.Where(s => s.Name == "similarity").Sum(s => s.Ms);

        return new IdentifyOutcome.Done(new IdentifyResult
        {
            ProbeJpeg = jpeg,
            Faces = probeFaces,
            Threshold = limits.Match,
            ExtractMs = report.TotalMs - searchMs,
            SearchMs = searchMs,
            GallerySize = gallery.Count,
            Report = report,
        });
    }

    // ---- attributes ----

    /// <summary>
    /// Attribute detection on every face, then a template and a 1:N gallery search per face
    /// (up to <see cref="MaxAttributeFaces"/>), timing each call.
    /// </summary>
    public static async Task<AttributeResult> AttributesAsync(byte[] jpeg, FaceLimits limits)
    {
        var steps = new List<CallStep>();
        if (!SdkSession.Ready)
        {
            return new AttributeResult
            {
                ProbeJpeg = jpeg,
                Threshold = limits.Match,
                Report = CallReport.Make("attribute", steps, SdkSession.Message, "", false),
            };
        }

        var gallery = Templates();
        var found = await Timed.RunAsync(
            steps,
            "faceDetection",
            async () => FaceJson.ParseDetect(
                await FaceSdk.DetectWithTemplatesAsync(
                    jpeg, SettingsStore.Current.AttributeFlags, MaxAttributeFaces, true)),
            f => $"{FacesDetail(f.Count)} · attributes",
            f => f.Count > 0).ConfigureAwait(false);
        SplitTemplateTime(steps, found);

        // The bridge already ordered them largest first, so the templates belong to the faces
        // this screen searches.
        var ordered = found;
        var searched = ordered.Take(MaxAttributeFaces).ToList();

        var features = new List<string>();
        if (searched.Count > 0)
        {
            foreach (var f in searched)
            {
                features.Add(f.Template ?? "");
            }
            steps.Add(new CallStep
            {
                Name = "templateExtraction",
                Ms = TemplateMs(found),
                Detail = $"{features.Count(f => f.Length > 0)} of {features.Count} templates",
                Ok = features.Any(f => f.Length > 0),
            });
        }

        var closest = new List<Candidate?>();
        if (features.Any(f => f.Length > 0))
        {
            closest = await Timed.RunAsync(
                steps,
                "similarity",
                async () =>
                {
                    var hits = new List<Candidate?>();
                    foreach (var feature in features)
                    {
                        hits.Add(feature.Length == 0 || gallery.Count == 0
                            ? null
                            : await ClosestAsync(feature, gallery).ConfigureAwait(false));
                    }
                    return hits;
                },
                _ => $"{gallery.Count} enrolled · {features.Count(f => f.Length > 0)} probes")
                .ConfigureAwait(false);
        }

        var faces = ordered.Select((box, i) => new FaceAttributes
        {
            Index = i,
            Box = box,
            TemplateBytes = i < features.Count ? Base64Bytes(features[i]) : 0,
            Closest = i < closest.Count ? closest[i] : null,
        }).ToArray();

        var bestHit = faces
            .Select(f => f.Closest)
            .Where(c => c != null)
            .OrderByDescending(c => c!.Similarity)
            .FirstOrDefault();
        var detail = faces.Length == 0
            ? "No face"
            : FacesDetail(faces.Length) +
              (bestHit == null ? "" : $" · closest {bestHit.Name} {bestHit.Similarity:F2}");

        return new AttributeResult
        {
            ProbeJpeg = jpeg,
            Faces = faces,
            Threshold = limits.Match,
            GallerySize = gallery.Count,
            Report = CallReport.Make("attribute", steps, detail, "", faces.Length > 0),
        };
    }

    // ---- helpers ----

    private static async Task<List<Candidate>> RankAsync(
        string feature,
        List<FaceRecord> gallery)
    {
        if (gallery.Count == 0)
        {
            return new List<Candidate>();
        }
        var scores = await FaceSdk.SimilarityManyAsync(
            feature,
            gallery.Select(g => g.Feature).ToArray()).ConfigureAwait(false);
        return gallery
            .Select((g, i) => new Candidate(g.Id, g.Name, g.Note, i < scores.Length ? scores[i] : -1))
            .OrderByDescending(c => c.Similarity)
            .ToList();
    }

    private static async Task<Candidate?> ClosestAsync(string feature, List<FaceRecord> gallery)
    {
        var ranked = await RankAsync(feature, gallery).ConfigureAwait(false);
        return ranked.Count > 0 ? ranked[0] : null;
    }

    /// <summary>Template time the bridge reported per face, summed.</summary>
    private static double TemplateMs(IReadOnlyList<FaceBox> faces) =>
        faces.Sum(f => f.TemplateMs);

    /// <summary>
    /// The detect step measured the whole call, templates included. This takes the template
    /// time back out so the two steps on the status card still read as separate work.
    /// </summary>
    private static void SplitTemplateTime(List<CallStep> steps, IReadOnlyList<FaceBox> faces)
    {
        var index = steps.FindLastIndex(s => s.Name == "faceDetection");
        if (index < 0)
        {
            return;
        }
        var step = steps[index];
        var detectOnly = step.Ms - TemplateMs(faces);
        steps[index] = new CallStep
        {
            Name = step.Name,
            Ms = detectOnly > 0 ? detectOnly : step.Ms,
            Detail = step.Detail,
            Ok = step.Ok,
        };
    }

    private static string FacesDetail(int n) => n == 1 ? "1 face" : $"{n} faces";

    /// <summary>Base64 length to byte count, so the status card can show the template size.</summary>
    private static int Base64Bytes(string b64)
    {
        if (string.IsNullOrEmpty(b64))
        {
            return 0;
        }
        var padding = b64.EndsWith("==", StringComparison.Ordinal) ? 2
            : b64.EndsWith("=", StringComparison.Ordinal) ? 1
            : 0;
        return Math.Max(0, (b64.Length * 3 / 4) - padding);
    }
}
