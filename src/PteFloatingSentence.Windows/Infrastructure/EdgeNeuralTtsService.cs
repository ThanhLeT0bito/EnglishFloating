using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace PteFloatingSentence.Windows.Infrastructure;

public interface IWebSocketClient : IDisposable
{
    WebSocketState State { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);

    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);

    Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);

    /// <summary>HTTP status of a refused handshake (e.g. 403), when known.</summary>
    int? HandshakeStatusCode => null;

    /// <summary>The server's Date header from the handshake response, when known.</summary>
    DateTimeOffset? ServerDate => null;
}

public sealed class EdgeNeuralTtsService : ITtsService
{
    public const string DefaultVoice = "en-US-JennyNeural";

    public static readonly string[] AvailableVoices =
    [
        "en-US-JennyNeural",
        "en-US-GuyNeural",
        "en-AU-NatashaNeural",
        "en-AU-WilliamNeural",
        "en-GB-SoniaNeural"
    ];

    public static string ResolveVoice(string? voice)
    {
        if (string.IsNullOrWhiteSpace(voice))
            return DefaultVoice;

        if (string.Equals(voice.Trim(), "random", StringComparison.OrdinalIgnoreCase))
        {
            return AvailableVoices[Random.Shared.Next(AvailableVoices.Length)];
        }

        return voice.Trim();
    }
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";
    public const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    public const string SecMsGecVersion = "1-143.0.3650.75";

    /// <summary>
    /// Read Aloud endpoints, newest first. Since October 2026 the Bing host refuses the handshake (HTTP 403) in many
    /// regions; Edge now uses api.msedgeservices.com, which takes the same token as Ocp-Apim-Subscription-Key.
    /// </summary>
    public static readonly IReadOnlyList<string> Endpoints =
    [
        "wss://api.msedgeservices.com/tts/cognitiveservices/websocket/v1?Ocp-Apim-Subscription-Key=" + TrustedClientToken,
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1?TrustedClientToken=" + TrustedClientToken
    ];

    private static readonly TimeSpan MaxClockError = TimeSpan.FromMinutes(1);

    private readonly Func<IWebSocketClient> _webSocketFactory;
    private readonly TimeSpan _timeout;

    /// <summary>Time one endpoint gets to accept the connection, so a host that hangs still leaves time for the next.</summary>
    private readonly TimeSpan _connectTimeout;

    /// <summary>The endpoint that answered last; later requests start there.</summary>
    private int _preferredEndpoint;

    /// <summary>Server time minus this PC's time, learned from a refused handshake. The token depends on the time.</summary>
    private TimeSpan _clockSkew;

    public EdgeNeuralTtsService()
        : this(() => new DefaultWebSocketClient())
    {
    }

    internal EdgeNeuralTtsService(Func<IWebSocketClient> webSocketFactory, TimeSpan? timeout = null, TimeSpan? connectTimeout = null)
    {
        _webSocketFactory = webSocketFactory ?? throw new ArgumentNullException(nameof(webSocketFactory));
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(7);
    }

    public static string FormatRate(double speed)
    {
        if (double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0)
        {
            return "+0%";
        }

        var percentage = (int)Math.Round((speed - 1.0) * 100);
        return percentage >= 0 ? $"+{percentage}%" : $"{percentage}%";
    }

    public static string BuildSsml(string text, string voice, double speed)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or whitespace.", nameof(text));
        }

        var selectedVoice = ResolveVoice(voice);
        var rateString = FormatRate(speed);
        var escapedText = SecurityElement.Escape(text.Trim()) ?? string.Empty;

        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><voice name='{selectedVoice}'><prosody rate='{rateString}'>{escapedText}</prosody></voice></speak>";
    }

    public static string GenerateSecMsGec() => GenerateSecMsGec(DateTimeOffset.UtcNow);

    /// <summary>The Sec-MS-GEC token for <paramref name="now"/>: SHA-256 of the 5-minute Windows tick count and the client token.</summary>
    public static string GenerateSecMsGec(DateTimeOffset now)
    {
        var unixSeconds = now.ToUnixTimeSeconds();
        var ticks = unixSeconds + 11644473600L;
        ticks -= ticks % 300L;
        ticks *= 10000000L;
        var strToHash = $"{ticks}{TrustedClientToken}";
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(strToHash));
        return Convert.ToHexString(hash);
    }

    public async Task<Stream> SynthesizeSpeechAsync(
        string text,
        string voice,
        double speed,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_timeout);
        try
        {
            return await SynthesizeCoreAsync(text, voice, speed, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Audio request timed out. Click to retry.");
        }
    }

    private async Task<Stream> SynthesizeCoreAsync(
        string text,
        string voice,
        double speed,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or whitespace.", nameof(text));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var ws = await ConnectAsync(cancellationToken);

        // 1. Send speech.config message
        var configMessage = "Content-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
                            "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
        var configBytes = Encoding.UTF8.GetBytes(configMessage);
        await ws.SendAsync(new ArraySegment<byte>(configBytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

        // 2. Send SSML message
        var requestId = Guid.NewGuid().ToString("N");
        var ssml = BuildSsml(text, voice, speed);
        var ssmlMessage = $"X-RequestId:{requestId}\r\nContent-Type:application/ssml+xml\r\nPath:ssml\r\n\r\n{ssml}";
        var ssmlBytes = Encoding.UTF8.GetBytes(ssmlMessage);
        await ws.SendAsync(new ArraySegment<byte>(ssmlBytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

        // 3. Receive responses into output stream
        var outputStream = new MemoryStream();
        try
        {
            var buffer = new byte[16384];
            using var packetBuffer = new MemoryStream();
            var turnEnded = false;

            while (!turnEnded && ws.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                packetBuffer.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    packetBuffer.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var textPayload = Encoding.UTF8.GetString(packetBuffer.GetBuffer(), 0, (int)packetBuffer.Length);
                    if (textPayload.Contains("Path:turn.end", StringComparison.OrdinalIgnoreCase))
                    {
                        turnEnded = true;
                        break;
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Binary)
                {
                    if (packetBuffer.TryGetBuffer(out ArraySegment<byte> segment) && segment.Count > 2)
                    {
                        var span = segment.AsSpan();
                        var headerLength = (ushort)((span[0] << 8) | span[1]);
                        var audioStart = 2 + headerLength;
                        if (span.Length > audioStart)
                        {
                            var audioSpan = span.Slice(audioStart);
                            outputStream.Write(audioSpan);
                        }
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!turnEnded)
            {
                throw new InvalidOperationException("TTS connection closed unexpectedly before synthesis finished.");
            }

            if (outputStream.Length == 0)
            {
                throw new InvalidOperationException("TTS synthesis finished without producing audio data.");
            }

            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Completed", cancellationToken);
                }
                catch
                {
                    // Best effort graceful close
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            outputStream.Position = 0;
            return outputStream;
        }
        catch
        {
            outputStream.Dispose();
            throw;
        }
    }

    public static Uri BuildUri(string endpoint, string connectionId, DateTimeOffset now) =>
        new($"{endpoint}&ConnectionId={connectionId}&Sec-MS-GEC={GenerateSecMsGec(now)}&Sec-MS-GEC-Version={SecMsGecVersion}");

    /// <summary>
    /// Opens a socket on the first endpoint that accepts the handshake, starting with the one that worked last.
    /// A refused handshake with a server time far from this PC's clock is retried once with the corrected time.
    /// </summary>
    private async Task<IWebSocketClient> ConnectAsync(CancellationToken cancellationToken)
    {
        WebSocketException? lastError = null;
        var timedOut = false;
        var start = _preferredEndpoint;
        for (var offset = 0; offset < Endpoints.Count; offset++)
        {
            var index = (start + offset) % Endpoints.Count;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var ws = _webSocketFactory();
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connect.CancelAfter(_connectTimeout);
                try
                {
                    var uri = BuildUri(Endpoints[index], Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow + _clockSkew);
                    await ws.ConnectAsync(uri, connect.Token);
                    _preferredEndpoint = index;
                    return ws;
                }
                catch (OperationCanceledException) when (connect.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    // This host does not answer: try the next one.
                    ws.Dispose();
                    timedOut = true;
                    break;
                }
                catch (WebSocketException error) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = error;
                    var refused = ws.HandshakeStatusCode is 401 or 403;
                    var serverDate = ws.ServerDate;
                    ws.Dispose();
                    if (attempt == 0 && refused && serverDate is { } date && LearnClockSkew(date))
                        continue;
                    break;
                }
                catch
                {
                    ws.Dispose();
                    throw;
                }
            }
        }

        if (lastError is not null)
            throw lastError;
        if (timedOut)
            throw new TimeoutException("Audio request timed out. Click to retry.");
        throw new WebSocketException("Could not connect to the text-to-speech service.");
    }

    /// <summary>Remembers how far this PC's clock is from <paramref name="serverDate"/>; true when that changed the token time.</summary>
    private bool LearnClockSkew(DateTimeOffset serverDate)
    {
        var skew = serverDate - DateTimeOffset.UtcNow;
        if ((skew - _clockSkew).Duration() < MaxClockError)
            return false;

        _clockSkew = skew;
        return true;
    }

    private sealed class DefaultWebSocketClient : IWebSocketClient
    {
        private readonly ClientWebSocket _ws = new();

        public DefaultWebSocketClient()
        {
            // Keeps the status and headers of a refused handshake (for the clock check).
            _ws.Options.CollectHttpResponseDetails = true;
            _ws.Options.SetRequestHeader("User-Agent", UserAgent);
            _ws.Options.SetRequestHeader("Pragma", "no-cache");
            _ws.Options.SetRequestHeader("Cache-Control", "no-cache");
            _ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
            _ws.Options.SetRequestHeader("Accept-Encoding", "gzip, deflate, br, zstd");
            _ws.Options.SetRequestHeader("Accept-Language", "en-US,en;q=0.9");
            var muid = Guid.NewGuid().ToString("N").ToUpperInvariant();
            _ws.Options.SetRequestHeader("Cookie", $"muid={muid};");
        }

        public WebSocketState State => _ws.State;

        public int? HandshakeStatusCode => _ws.HttpStatusCode == 0 ? null : (int)_ws.HttpStatusCode;

        public DateTimeOffset? ServerDate =>
            _ws.HttpResponseHeaders is { } headers
            && headers.TryGetValue("Date", out var values)
            && DateTimeOffset.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
                ? date
                : null;

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
            _ws.ConnectAsync(uri, cancellationToken);

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            _ws.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            _ws.ReceiveAsync(buffer, cancellationToken);

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
            _ws.CloseAsync(closeStatus, statusDescription, cancellationToken);

        public void Dispose()
        {
            try
            {
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                {
                    _ws.Abort();
                }
            }
            catch
            {
                // Best effort abort before dispose
            }
            finally
            {
                _ws.Dispose();
            }
        }
    }
}
