using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FaceRecognition.Net.Maui;

/// <summary>
/// One timed SDK call. <see cref="Ok"/> is false when this step stopped the action.
/// </summary>
public sealed class CallStep
{
    /// <summary>
    /// A stable identifier, not a label: pages translate it for display. One of
    /// <c>faceDetection</c>, <c>templateExtraction</c>, <c>similarity</c>,
    /// <c>livenessCheck</c>, <c>duplicateCheck</c>, <c>save</c>.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("ms")]
    public double Ms { get; init; }

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = "";

    [JsonPropertyName("ok")]
    public bool Ok { get; init; } = true;
}

/// <summary>
/// What an action did: total time, each call, progress, detail and overall outcome. Shown by
/// the status card on every screen, and stored with each enrolled person.
/// </summary>
public sealed class CallReport
{
    [JsonPropertyName("function")]
    public string Function { get; init; } = "";

    [JsonPropertyName("steps")]
    public IReadOnlyList<CallStep> Steps { get; init; } = Array.Empty<CallStep>();

    [JsonPropertyName("progress")]
    public string Progress { get; init; } = "";

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; init; } = true;

    /// <summary>Wall-clock time the action finished (epoch ms).</summary>
    [JsonPropertyName("finishedAt")]
    public long FinishedAt { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonIgnore]
    public double TotalMs => Steps.Sum(s => double.IsFinite(s.Ms) ? s.Ms : 0);

    [JsonIgnore]
    public DateTime? FinishedAtLocal => FinishedAt > 0
        ? DateTimeOffset.FromUnixTimeMilliseconds(FinishedAt).LocalDateTime
        : null;

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Null for empty or unreadable JSON, so a person saved before reports were stored simply
    /// has no card rather than breaking the sheet.
    /// </summary>
    public static CallReport? FromJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<CallReport>(raw, Json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a report. <paramref name="progress"/> defaults to "n of n · last step", or
    /// "ok of n · stopped at step" when the action failed.
    /// </summary>
    public static CallReport Make(
        string function,
        IReadOnlyList<CallStep> steps,
        string detail,
        string progress = "",
        bool success = true)
    {
        var shown = progress;
        if (string.IsNullOrEmpty(shown))
        {
            shown = success
                ? $"{steps.Count} of {steps.Count} · {(steps.Count > 0 ? steps[^1].Name : "done")}"
                : $"{steps.Count(s => s.Ok)} of {steps.Count} · stopped at " +
                  $"{steps.FirstOrDefault(s => !s.Ok)?.Name ?? "start"}";
        }
        return new CallReport
        {
            Function = function,
            Steps = steps.ToArray(),
            Progress = shown,
            Detail = detail,
            Success = success,
        };
    }
}

/// <summary>Times an async block and appends a <see cref="CallStep"/> describing it.</summary>
internal static class Timed
{
    public static async Task<T> RunAsync<T>(
        List<CallStep> steps,
        string name,
        Func<Task<T>> block,
        Func<T, string> detail,
        Func<T, bool>? ok = null)
    {
        var watch = Stopwatch.StartNew();
        var value = await block().ConfigureAwait(false);
        watch.Stop();
        steps.Add(new CallStep
        {
            Name = name,
            Ms = watch.Elapsed.TotalMilliseconds,
            Detail = detail(value),
            Ok = ok?.Invoke(value) ?? true,
        });
        return value;
    }
}
