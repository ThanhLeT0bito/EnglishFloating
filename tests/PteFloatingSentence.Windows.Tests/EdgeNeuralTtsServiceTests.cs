using System.IO;
using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using PteFloatingSentence.Windows.Infrastructure;
using Xunit;

namespace PteFloatingSentence.Windows.Tests;

public class EdgeNeuralTtsServiceTests
{
    [Fact]
    public void BuildSsml_FormatsRateAndVoiceCorrectly()
    {
        var ssml = EdgeNeuralTtsService.BuildSsml("Hello world", "en-US-JennyNeural", 1.1);

        ssml.Should().Contain("en-US-JennyNeural");
        ssml.Should().Contain("+10%");
        ssml.Should().Contain("Hello world");
    }

    [Theory]
    [InlineData("en-US-GuyNeural")]
    [InlineData("en-AU-NatashaNeural")]
    [InlineData("en-AU-WilliamNeural")]
    [InlineData("en-GB-SoniaNeural")]
    public void BuildSsml_WithDifferentVoices_SetsVoiceAttribute(string voice)
    {
        var ssml = EdgeNeuralTtsService.BuildSsml("Sample text", voice, 1.0);

        ssml.Should().Contain($"<voice name='{voice}'>");
    }

    [Fact]
    public void BuildSsml_WhenVoiceIsNullOrEmpty_UsesDefaultJennyVoice()
    {
        var ssmlNull = EdgeNeuralTtsService.BuildSsml("Sample text", null!, 1.0);
        var ssmlEmpty = EdgeNeuralTtsService.BuildSsml("Sample text", "  ", 1.0);

        ssmlNull.Should().Contain("<voice name='en-US-JennyNeural'>");
        ssmlEmpty.Should().Contain("<voice name='en-US-JennyNeural'>");
    }

    [Theory]
    [InlineData(1.0, "+0%")]
    [InlineData(1.1, "+10%")]
    [InlineData(1.2, "+20%")]
    [InlineData(0.9, "-10%")]
    [InlineData(0.8, "-20%")]
    [InlineData(1.05, "+5%")]
    [InlineData(0.95, "-5%")]
    [InlineData(0.0, "+0%")]
    [InlineData(-1.0, "+0%")]
    public void FormatRate_CalculatesExpectedPercentages(double speed, string expectedRate)
    {
        var rate = EdgeNeuralTtsService.FormatRate(speed);

        rate.Should().Be(expectedRate);
    }

    [Fact]
    public void BuildSsml_EscapesSpecialXmlCharacters()
    {
        var rawText = "Tom & Jerry <cat> 'paws' \"whisker\"";
        var ssml = EdgeNeuralTtsService.BuildSsml(rawText, "en-US-JennyNeural", 1.0);

        ssml.Should().NotContain("Tom & Jerry");
        ssml.Should().Contain("Tom &amp; Jerry");
        ssml.Should().Contain("&lt;cat&gt;");
        ssml.Should().Contain("&apos;paws&apos;");
        ssml.Should().Contain("&quot;whisker&quot;");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildSsml_WithNullOrWhitespaceText_ThrowsArgumentException(string? invalidText)
    {
        var act = () => EdgeNeuralTtsService.BuildSsml(invalidText!, "en-US-JennyNeural", 1.0);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SynthesizeSpeechAsync_WithNullOrWhitespaceText_ThrowsArgumentException(string? invalidText)
    {
        var service = new EdgeNeuralTtsService();

        var act = () => service.SynthesizeSpeechAsync(invalidText!, "en-US-JennyNeural", 1.0, CancellationToken.None);

        await FluentActions.Awaiting(act).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SynthesizeSpeechAsync_WhenAlreadyCancelled_ThrowsOperationCanceledException()
    {
        var service = new EdgeNeuralTtsService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.SynthesizeSpeechAsync("Valid sentence", "en-US-JennyNeural", 1.0, cts.Token);

        await FluentActions.Awaiting(act).Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SynthesizeSpeechAsync_WhenConnectOrReceiveStalls_ThrowsTimeoutExceptionAndDisposesSocket(bool blockConnect)
    {
        var socket = new BlockingWebSocketClient(blockConnect);
        var service = new EdgeNeuralTtsService(() => socket, TimeSpan.FromMilliseconds(20));

        var act = () => service.SynthesizeSpeechAsync("Test", "en-US-JennyNeural", 1.0);

        await FluentActions.Awaiting(act).Should().ThrowAsync<TimeoutException>()
            .WithMessage("Audio request timed out. Click to retry.");
        socket.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSpeechAsync_WhenCallerCancelsFirst_PreservesOperationCanceledException()
    {
        var socket = new BlockingWebSocketClient(blockConnect: false);
        var service = new EdgeNeuralTtsService(() => socket, TimeSpan.FromSeconds(5));
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        var act = () => service.SynthesizeSpeechAsync("Test", "en-US-JennyNeural", 1.0, caller.Token);

        await FluentActions.Awaiting(act).Should().ThrowAsync<OperationCanceledException>();
        socket.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSpeechAsync_ReceivesAudioChunksAndTurnEnd_ReturnsCombinedMemoryStream()
    {
        var fakeSocket = new FakeWebSocketClient();

        // 1. First binary audio message: 2-byte header length + header + 3 audio bytes [0x11, 0x22, 0x33]
        fakeSocket.EnqueueBinaryMessage(header: "Path:audio\r\n", audioPayload: [0x11, 0x22, 0x33]);

        // 2. Intermediate text message
        fakeSocket.EnqueueTextMessage("Path:response\r\nContent-Type:text/plain\r\n\r\nIn progress");

        // 3. Second binary audio message: 2-byte header length + header + 2 audio bytes [0x44, 0x55]
        fakeSocket.EnqueueBinaryMessage(header: "Path:audio\r\n", audioPayload: [0x44, 0x55]);

        // 4. Turn end text message
        fakeSocket.EnqueueTextMessage("Path:turn.end\r\nContent-Type:text/plain\r\n\r\nEnd");

        var service = new EdgeNeuralTtsService(() => fakeSocket);

        using var resultStream = await service.SynthesizeSpeechAsync("Hello test", "en-US-JennyNeural", 1.0, CancellationToken.None);

        resultStream.Should().NotBeNull();
        resultStream.Position.Should().Be(0);
        resultStream.Length.Should().Be(5);

        using var memory = new MemoryStream();
        await resultStream.CopyToAsync(memory);
        memory.ToArray().Should().Equal([0x11, 0x22, 0x33, 0x44, 0x55]);

        // Verify sent messages: speech.config first, then ssml
        fakeSocket.SentMessages.Should().HaveCount(2);
        fakeSocket.SentMessages[0].Should().Contain("Path:speech.config");
        fakeSocket.SentMessages[0].Should().Contain("audio-24khz-48kbitrate-mono-mp3");

        fakeSocket.SentMessages[1].Should().Contain("Path:ssml");
        fakeSocket.SentMessages[1].Should().Contain("<voice name='en-US-JennyNeural'>");
        fakeSocket.SentMessages[1].Should().Contain("Hello test");

        fakeSocket.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSpeechAsync_WhenServerClosesWithoutAudio_ThrowsInvalidOperationException()
    {
        var fakeSocket = new FakeWebSocketClient();
        // Server closes immediately without sending any audio
        fakeSocket.EnqueueClose(WebSocketCloseStatus.NormalClosure, "Closed without audio");

        var service = new EdgeNeuralTtsService(() => fakeSocket);

        var act = () => service.SynthesizeSpeechAsync("Hello test", "en-US-JennyNeural", 1.0, CancellationToken.None);

        await FluentActions.Awaiting(act).Should().ThrowAsync<InvalidOperationException>();
        fakeSocket.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSpeechAsync_WhenSocketThrows_DisposesSocketAndPropagatesException()
    {
        var fakeSocket = new FakeWebSocketClient
        {
            ThrowOnReceive = new WebSocketException("Simulated socket error")
        };

        var service = new EdgeNeuralTtsService(() => fakeSocket);

        var act = () => service.SynthesizeSpeechAsync("Hello test", "en-US-JennyNeural", 1.0, CancellationToken.None);

        await FluentActions.Awaiting(act).Should().ThrowAsync<WebSocketException>();
        fakeSocket.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task SynthesizeSpeechAsync_LiveEndpoint_CanSynthesizeRealAudio()
    {
        var service = new EdgeNeuralTtsService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var stream = await service.SynthesizeSpeechAsync("Test", "en-US-JennyNeural", 1.0, cts.Token);
        stream.Should().NotBeNull();
        stream.Length.Should().BeGreaterThan(100);
        stream.Position.Should().Be(0);
    }

    private sealed class BlockingWebSocketClient(bool blockConnect) : IWebSocketClient
    {
        public bool IsDisposed { get; private set; }
        public WebSocketState State => WebSocketState.Open;

        public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            if (blockConnect)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after cancellation.");
        }

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeWebSocketClient : IWebSocketClient
    {
        private readonly Queue<Func<ArraySegment<byte>, WebSocketReceiveResult>> _receiveQueue = new();
        public List<string> SentMessages { get; } = [];
        public bool IsDisposed { get; private set; }
        public WebSocketState State { get; private set; } = WebSocketState.Open;
        public Exception? ThrowOnReceive { get; set; }

        public void EnqueueTextMessage(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _receiveQueue.Enqueue(buffer =>
            {
                bytes.CopyTo(buffer.Array!, buffer.Offset);
                return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
            });
        }

        public void EnqueueBinaryMessage(string header, byte[] audioPayload)
        {
            var headerBytes = Encoding.UTF8.GetBytes(header);
            var headerLength = (ushort)headerBytes.Length;

            var packet = new byte[2 + headerLength + audioPayload.Length];
            packet[0] = (byte)((headerLength >> 8) & 0xFF);
            packet[1] = (byte)(headerLength & 0xFF);
            Buffer.BlockCopy(headerBytes, 0, packet, 2, headerLength);
            Buffer.BlockCopy(audioPayload, 0, packet, 2 + headerLength, audioPayload.Length);

            _receiveQueue.Enqueue(buffer =>
            {
                packet.CopyTo(buffer.Array!, buffer.Offset);
                return new WebSocketReceiveResult(packet.Length, WebSocketMessageType.Binary, true);
            });
        }

        public void EnqueueClose(WebSocketCloseStatus status, string description)
        {
            _receiveQueue.Enqueue(_ =>
            {
                State = WebSocketState.CloseReceived;
                return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, status, description);
            });
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            State = WebSocketState.Open;
            return Task.CompletedTask;
        }

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
            SentMessages.Add(text);
            return Task.CompletedTask;
        }

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ThrowOnReceive != null)
            {
                throw ThrowOnReceive;
            }

            if (_receiveQueue.Count > 0)
            {
                var func = _receiveQueue.Dequeue();
                return Task.FromResult(func(buffer));
            }

            State = WebSocketState.Closed;
            return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "End"));
        }

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        {
            State = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            IsDisposed = true;
            State = WebSocketState.Closed;
        }
    }
}
