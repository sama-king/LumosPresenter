using System.Net.Http.Json;
using System.Text.Json;

namespace LumosPresenter.Launcher;

/// <summary>One selectable microphone, as reported by the server.</summary>
public sealed record MicrophoneDevice(int Id, string Name, bool IsDefault);

/// <summary>
/// Thin HTTP client over the WebHost's own API. The launcher deliberately owns no audio
/// logic: the server holds the capture device, so asking it keeps the tray, the window
/// and the console in agreement.
/// </summary>
public sealed class ServerClient(int port)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri($"http://localhost:{port}/"),
        Timeout = TimeSpan.FromSeconds(3),
    };

    /// <summary>True when the WebHost is up and answering.</summary>
    public async Task<bool> IsRunningAsync()
    {
        try
        {
            using var response = await _http.GetAsync("healthz");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the pipeline is currently listening, or null when the server cannot be
    /// reached. The server owns this state, so the tray, the window and the web console
    /// all read it from here rather than each tracking their own idea of it.
    /// </summary>
    public async Task<bool?> IsListeningAsync()
    {
        try
        {
            var payload = await _http.GetFromJsonAsync<JsonElement>("api/status");
            return payload.TryGetProperty("listening", out var listening)
                && listening.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? listening.GetBoolean()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts or stops listening, returning null on success or a message to show. Starting
    /// can fail on the engine or the capture device, so the reason is surfaced rather than
    /// swallowed — mirroring <see cref="SelectMicrophoneAsync"/>.
    /// </summary>
    public async Task<string?> SetListeningAsync(bool on)
    {
        try
        {
            using var response = await _http.PostAsync(on ? "api/listening/start" : "api/listening/stop", null);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }
            // A failed start usually carries a message; fall back to the status code.
            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (body.TryGetProperty("message", out var message) && message.GetString() is { } text)
                {
                    return text;
                }
            }
            catch (Exception)
            {
                // Not a JSON body — use the generic wording below.
            }
            return on ? "Could not start listening." : "Could not stop listening.";
        }
        catch (Exception)
        {
            return "Server not reachable.";
        }
    }

    /// <summary>The available microphones and which one is selected, or empty when unreachable.</summary>
    public async Task<(IReadOnlyList<MicrophoneDevice> Devices, int? Selected)> GetMicrophonesAsync()
    {
        try
        {
            var payload = await _http.GetFromJsonAsync<JsonElement>("api/audio/devices");
            var selected = payload.TryGetProperty("selected", out var s) && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32()
                : (int?)null;
            var devices = payload.TryGetProperty("devices", out var d)
                ? d.Deserialize<List<MicrophoneDevice>>(Json) ?? []
                : [];
            // Until a device is explicitly chosen the server reports none; capture still
            // uses the system default, so show that as selected rather than nothing.
            selected ??= devices.FirstOrDefault(device => device.IsDefault)?.Id;
            return (devices, selected);
        }
        catch (Exception)
        {
            return ([], null);
        }
    }

    /// <summary>
    /// Selects the input device. The server refuses while listening — it returns 400 with
    /// a message — so the reason is surfaced rather than swallowed.
    /// </summary>
    public async Task<string?> SelectMicrophoneAsync(int deviceId)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("api/audio/device", new { deviceId });
            if (response.IsSuccessStatusCode)
            {
                return null;
            }
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.TryGetProperty("message", out var message)
                ? message.GetString()
                : "Could not change the microphone.";
        }
        catch (Exception)
        {
            return "Server not reachable.";
        }
    }
}
