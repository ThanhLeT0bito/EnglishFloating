# Edge Neural TTS Audio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide natural native-speaker audio playback for sentences on the floating widget using Microsoft Edge Neural TTS with smart local caching and voice/speed settings.

**Architecture:** A domain settings layer stores voice and speed preferences. `AudioCacheManager` manages LRU disk cache (~20 MB limit). `EdgeNeuralTtsService` communicates with Microsoft's Edge TTS service over `ClientWebSocket` to stream lightweight MP3 audio. `FloatingWindow` exposes a compact speaker button next to the sentence index and manages playback through `MediaPlayer` with strict cancellation and disposal semantics.

**Tech Stack:** .NET 9.0, WPF, `System.Net.WebSockets.ClientWebSocket`, `System.Windows.Media.MediaPlayer`, xUnit, FluentAssertions.

## Global Constraints

- Target frameworks: `net9.0` (Core) and `net9.0-windows` (Windows).
- Default voice: `"en-US-JennyNeural"`.
- Default speed: `1.0` (range: `0.8` to `1.2`).
- Default cache limit: 20 MB (`20 * 1024 * 1024` bytes).
- Memory leak prevention: explicit lifecycle management for `MediaPlayer`, `ClientWebSocket`, event subscriptions, and `CancellationTokenSource`.

---

### Task 1: TTS Settings Domain & Persistence

**Files:**
- Modify: `src/PteFloatingSentence.Core/AppSettings.cs`
- Modify: `src/PteFloatingSentence.Core/SettingsUpdateMerger.cs`
- Test: `tests/PteFloatingSentence.Core.Tests/SettingsUpdateMergerTests.cs` (or `JsonSettingsStoreTests.cs`)

**Interfaces:**
- `AppSettings`:
  - `public string TtsVoice { get; init; } = "en-US-JennyNeural";`
  - `public double TtsSpeed { get; init; } = 1.0;`
  - `public long TtsMaxCacheSizeBytes { get; init; } = 20 * 1024 * 1024;`
- `SettingsUpdateMerger.MergeEditableFields(AppSettings latest, AppSettings submitted)` merges `TtsVoice` and `TtsSpeed`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void MergeEditableFields_PreservesTtsVoiceAndSpeed()
{
    var original = AppSettings.Default with { TtsVoice = "en-US-JennyNeural", TtsSpeed = 1.0 };
    var submitted = original with { TtsVoice = "en-AU-NatashaNeural", TtsSpeed = 1.1 };

    var merged = SettingsUpdateMerger.MergeEditableFields(original, submitted);

    merged.TtsVoice.Should().Be("en-AU-NatashaNeural");
    merged.TtsSpeed.Should().Be(1.1);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PteFloatingSentence.Core.Tests --filter "FullyQualifiedName~MergeEditableFields_PreservesTtsVoiceAndSpeed"`
Expected: FAIL due to missing `TtsVoice` / `TtsSpeed` properties.

- [ ] **Step 3: Implement minimal code**

Add `TtsVoice`, `TtsSpeed`, `TtsMaxCacheSizeBytes` properties to `AppSettings.cs` and merge them in `SettingsUpdateMerger.cs`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PteFloatingSentence.Core.Tests --filter "FullyQualifiedName~MergeEditableFields_PreservesTtsVoiceAndSpeed"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PteFloatingSentence.Core tests/PteFloatingSentence.Core.Tests
git commit -m "feat(tts): add TTS voice and speed configuration to AppSettings"
```

---

### Task 2: Smart Audio Cache Manager

**Files:**
- Create: `src/PteFloatingSentence.Windows/Infrastructure/AudioCacheManager.cs`
- Create: `src/PteFloatingSentence.Windows/Infrastructure/IAudioCacheManager.cs`
- Test: `tests/PteFloatingSentence.Windows.Tests/AudioCacheManagerTests.cs`

**Interfaces:**
- `IAudioCacheManager`:
  - `string GetCacheFilePath(string text, string voice, double speed);`
  - `bool TryGetCachedAudio(string text, string voice, double speed, out string filePath);`
  - `Task SaveAudioAsync(string text, string voice, double speed, Stream audioStream, CancellationToken cancellationToken);`
  - `long GetTotalCacheSizeBytes();`
  - `void ClearCache();`
  - `void PruneToLimit(long maxSizeBytes);`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void GetCacheFilePath_ReturnsConsistentNormalizedMd5Path()
{
    var cache = new AudioCacheManager(testDir);
    var p1 = cache.GetCacheFilePath("Hello world", "en-US-JennyNeural", 1.0);
    var p2 = cache.GetCacheFilePath("Hello world", "en-US-JennyNeural", 1.0);
    var p3 = cache.GetCacheFilePath("Hello world", "en-US-JennyNeural", 1.1);

    p1.Should().Be(p2);
    p1.Should().NotBe(p3);
    p1.Should().EndWith(".mp3");
}

[Fact]
public async Task SaveAudioAsync_PrunesOldestWhenExceedingLimit()
{
    // test save and verify LRU eviction when size exceeds limit
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests --filter "FullyQualifiedName~AudioCacheManagerTests"`
Expected: FAIL due to missing `AudioCacheManager`.

- [ ] **Step 3: Implement AudioCacheManager**

Implement `IAudioCacheManager` with MD5 hashing, atomic file writing (temp file -> rename), file size calculation, and LRU pruning by `LastAccessTimeUtc`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests --filter "FullyQualifiedName~AudioCacheManagerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PteFloatingSentence.Windows/Infrastructure/ tests/PteFloatingSentence.Windows.Tests/
git commit -m "feat(tts): implement Smart AudioCacheManager with LRU pruning"
```

---

### Task 3: Edge Neural TTS Service

**Files:**
- Create: `src/PteFloatingSentence.Windows/Infrastructure/ITtsService.cs`
- Create: `src/PteFloatingSentence.Windows/Infrastructure/EdgeNeuralTtsService.cs`
- Test: `tests/PteFloatingSentence.Windows.Tests/EdgeNeuralTtsServiceTests.cs`

**Interfaces:**
- `ITtsService`:
  - `Task<Stream> SynthesizeSpeechAsync(string text, string voice, double speed, CancellationToken cancellationToken);`
- `EdgeNeuralTtsService`:
  - Formats SSML with rate percentage (e.g. `+10%`, `-10%`, `+0%`).
  - Connects to `wss://speech.platform.bing.com/consumer/speech/synthesize/readahead/edge/v1?TrustedClientToken=6A5AA1D4EA654941A3D44C6D7E847D48`.
  - Parses binary audio stream responses delimited by `Path:audio\r\n`.
  - Completes when `Path:turn.end` is received or cancellation is signaled.

- [ ] **Step 1: Write unit tests for SSML generation and request formatting**

```csharp
[Fact]
public void BuildSsml_FormatsRateAndVoiceCorrectly()
{
    var ssml = EdgeNeuralTtsService.BuildSsml("Hello world", "en-US-JennyNeural", 1.1);
    ssml.Should().Contain("en-US-JennyNeural");
    ssml.Should().Contain("+10%");
    ssml.Should().Contain("Hello world");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests --filter "FullyQualifiedName~EdgeNeuralTtsServiceTests"`
Expected: FAIL due to missing `EdgeNeuralTtsService`.

- [ ] **Step 3: Implement EdgeNeuralTtsService**

Implement `ITtsService`, SSML builder, WebSocket connection with handshake headers, and streaming packet aggregator into `MemoryStream`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests --filter "FullyQualifiedName~EdgeNeuralTtsServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PteFloatingSentence.Windows/Infrastructure/ tests/PteFloatingSentence.Windows.Tests/
git commit -m "feat(tts): implement EdgeNeuralTtsService with SSML builder"
```

---

### Task 4: Audio Player & Floating Window UI

**Files:**
- Create: `src/PteFloatingSentence.Windows/Infrastructure/IAudioPlayer.cs`
- Create: `src/PteFloatingSentence.Windows/Infrastructure/WpfAudioPlayer.cs`
- Modify: `src/PteFloatingSentence.Windows/FloatingWindow.xaml`
- Modify: `src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs`
- Test: `tests/PteFloatingSentence.Windows.Tests/FloatingAudioWorkflowTests.cs`

**Interfaces:**
- `IAudioPlayer`:
  - `Task PlayFileAsync(string filePath, Action onEnded, Action<Exception> onError);`
  - `void Stop();`
  - `bool IsPlaying { get; }`
  - `void Dispose();`
- `FloatingWindow`:
  - Speaker button `AudioButton` placed next to `ProgressText`.
  - Click handler: checks cache -> fetches if missing -> plays audio.
  - Automatically cancels and stops playback on Next/Previous sentence navigation, review practice mode start, or window closing.

- [ ] **Step 1: Write workflow tests for player and navigation cancellation**

```csharp
[Fact]
public void NavigatingToNextSentence_StopsActiveAudioPlayback()
{
    // test audio state resets when sentence index changes
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests --filter "FullyQualifiedName~FloatingAudioWorkflowTests"`
Expected: FAIL.

- [ ] **Step 3: Implement Audio Player and FloatingWindow UI**

- Implement `WpfAudioPlayer` wrapping `MediaPlayer` with lifecycle management (`MediaEnded`, `MediaFailed`, `Close`, `Dispose`).
- Add `AudioButton` style and element in `FloatingWindow.xaml`.
- Wire up `AudioButton_Click`, background TTS fetch with cache, state updating, and cancellation token on sentence advance/exit.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests --filter "FullyQualifiedName~FloatingAudioWorkflowTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PteFloatingSentence.Windows tests/PteFloatingSentence.Windows.Tests
git commit -m "feat(tts): integrate audio button on FloatingWindow with playback cancellation"
```

---

### Task 5: Settings Center Integration (DisplayPage)

**Files:**
- Modify: `src/PteFloatingSentence.Windows/DisplayPage.xaml`
- Modify: `src/PteFloatingSentence.Windows/DisplayPage.xaml.cs`
- Modify: `src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs`
- Test: `tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs`

**Interfaces:**
- `DisplayPage`:
  - `VoiceAccentComboBox`: choices `en-US-JennyNeural`, `en-US-GuyNeural`, `en-AU-NatashaNeural`, `en-AU-WilliamNeural`, `en-GB-SoniaNeural`.
  - `SpeedSlider`: range 0.8 - 1.2, value label `0.80x` - `1.20x`.
  - `ClearAudioCacheButton`: clears cache and updates `CacheSizeLabel`.

- [ ] **Step 1: Write failing test in SettingsWorkflowTests**

```csharp
[Fact]
public void DisplayPage_UpdatesTtsVoiceAndSpeed_WhenChanged()
{
    // verify setting changes are saved to AppSettings
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests --filter "FullyQualifiedName~DisplayPage_UpdatesTtsVoiceAndSpeed"`
Expected: FAIL.

- [ ] **Step 3: Implement DisplayPage controls and binding**

Replace the "Coming soon" placeholder in `DisplayPage.xaml` with active Voice combo, Speed slider with formatted text, and Clear Cache button. Bind to `AppSettings.TtsVoice` and `AppSettings.TtsSpeed`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PteFloatingSentence.Windows.Tests --filter "FullyQualifiedName~DisplayPage_UpdatesTtsVoiceAndSpeed"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/PteFloatingSentence.Windows tests/PteFloatingSentence.Windows.Tests
git commit -m "feat(tts): add Voice, Speed, and Cache management to DisplayPage"
```

---

### Task 6: Full Verification & Build Validation

**Files:**
- Verification only

- [ ] **Step 1: Run full test suite**

Run: `cmd /c "set DOTNET_ROOT=C:\Program Files\dotnet && dotnet test"`
Expected: All tests pass (Core + Windows).

- [ ] **Step 2: Build release executable**

Run: `powershell -File scripts/Publish-WindowsRelease.ps1`
Expected: Successful build of `release/EnglishFloating.exe`.

- [ ] **Step 3: Commit verification evidence**

```bash
git commit --allow-empty -m "chore: record TTS audio feature verification"
```
