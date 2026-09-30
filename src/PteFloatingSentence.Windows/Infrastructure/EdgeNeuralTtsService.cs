using System.IO;
using System.Net.WebSockets;
using System.Security;
using System.Text;

namespace PteFloatingSentence.Windows.Infrastructure;

public interface IWebSocketClient : IDisposable
{
    WebSocketState State { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);

    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);

    Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
}

public sealed class EdgeNeuralTtsService : ITtsService
{
    public const string DefaultVoice = "en-US-JennyNeural";
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0";
    private const string BaseEndpoint = "wss://speech.platform.bing.com/consumer/speech/synthesize/readahead/edge/v1?TrustedClientToken=6A5AA1D4EA654941A3D44C6D7E847D48";

    private readonly Func<IWebSocketClient> _webSocketFactory;

    public EdgeNeuralTtsService()
        : this(() => new DefaultWebSocketClient())
    {
    }

    internal EdgeNeuralTtsService(Func<IWebSocketClient> webSocketFactory)
    {
        _webSocketFactory = webSocketFactory ?? throw new ArgumentNullException(nameof(webSocketFactory));
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

        var selectedVoice = string.IsNullOrWhiteSpace(voice) ? DefaultVoice : voice.Trim();
        var rateString = FormatRate(speed);
        var escapedText = SecurityElement.Escape(text.Trim()) ?? string.Empty;

        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'><voice name='{selectedVoice}'><prosody rate='{rateString}'>{escapedText}</prosody></voice></speak>";
    }

    public async Task<Stream> SynthesizeSpeechAsync(
        string text,
        string voice,
        double speed,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Text cannot be null or whitespace.", nameof(text));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var connectionId = Guid.NewGuid().ToString("N");
        var uri = new Uri($"{BaseEndpoint}&ConnectionId={connectionId}");

        using var ws = _webSocketFactory();
        await ws.ConnectAsync(uri, cancellationToken);

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
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Completed", CancellationToken.None);
                }
                catch
                {
                    // Best effort graceful close
                }
            }

            outputStream.Position = 0;
            return outputStream;
        }
        catch
        {
            outputStream.Dispose();
            throw;
        }
    }

    private sealed class DefaultWebSocketClient : IWebSocketClient
    {
        private readonly ClientWebSocket _ws = new();

        public DefaultWebSocketClient()
        {
            _ws.Options.SetRequestHeader("User-Agent", UserAgent);
            _ws.Options.SetRequestHeader("Pragma", "no-cache");
            _ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        }

        public WebSocketState State => _ws.State;

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
