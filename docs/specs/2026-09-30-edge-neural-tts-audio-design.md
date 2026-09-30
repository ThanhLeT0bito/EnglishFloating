# Edge Neural TTS Audio Design

## Goal

Provide natural native-speaker audio playback for the active sentence on the floating widget using Microsoft Edge Neural TTS. Keep widget interface minimal and unobtrusive with a small speaker icon, while offering voice accent and speed preferences in Settings.

## User Flow

1. On the floating widget in normal sentence view, a compact speaker icon (`🔊`) appears alongside the progress indicator (e.g., next to `1/10`).
2. When the user clicks the speaker icon:
   - If audio is already playing, playback stops immediately (toggle play/stop).
   - If audio is not playing:
     - The icon enters a loading/playing state (`&#xE768;` or active glow).
     - The app checks the local audio cache. If cached, it plays immediately (0ms).
     - If not cached, the app fetches the audio stream via Edge Neural TTS in the background, writes it to the local cache, and begins playback.
   - When playback completes or fails, the icon returns to the idle state.
3. If the user navigates to another sentence (Next/Previous), exits practice, or hides the overlay while audio is loading or playing, playback and any in-flight download are immediately cancelled, avoiding background noise or resource leaks.
4. In Settings Center:
   - A dedicated **Speech Audio (TTS)** section allows choosing Voice Accent (`en-US-JennyNeural`, `en-US-GuyNeural`, `en-AU-NatashaNeural`, `en-AU-WilliamNeural`, `en-GB-SoniaNeural`) and Speed (`0.8x` to `1.2x`, default `1.0x`).
   - A **Clear Audio Cache** button displays current cache size and allows purging downloaded files on demand.

## TTS Service & Protocol

- **Protocol**: Microsoft Edge Neural TTS endpoint (standard WebSocket communication with binary MP3 streaming, using Edge client token/headers, requiring no third-party API key).
- **Audio Format**: `audio-24khz-48kbitrate-mono-mp3`, lightweight (~15 KB to 30 KB per sentence).
- **Fallback**: Gracefully handle network timeouts or offline errors by restoring button state and showing a non-blocking tooltip or visual feedback without crashing the app.

## Data and Settings Model

- Add `TtsSettings` to `AppSettings`:
  - `Voice`: string (default `"en-US-JennyNeural"`).
  - `Speed`: double (default `1.0`, range `0.8` to `1.2`).
  - `MaxCacheSizeBytes`: long (default 20 MB = `20 * 1024 * 1024`).
- Settings persistence reuses existing `JsonSettingsStore` and `SettingsUpdateMerger` with backward-compatible defaults.

## Audio Cache Architecture

- **Location**: `%LocalAppData%\PteFloatingSentence\audio_cache\`.
- **Cache Key**: MD5 hex hash of `[Text]_[Voice]_[Speed]`.
- **Cache Pruning**: When total cache size exceeds 20 MB after saving a new file, prune oldest accessed files (LRU) until size is under the limit.
- **Cache Purge**: `ClearCache()` removes all files in `audio_cache` directory and updates the UI size label.

## Floating Widget Integration

- Placed adjacent to `ProgressText` in `NormalSentenceContainer`:
  - Does not occupy text width or interfere with `SentenceBox` or vocabulary spans.
  - Hover and active visual feedback consistent with the dark theme and existing `CircleNavButtonStyle` / `CardActionButtonStyle` palette.
- Only visible in normal reading mode (hidden or disabled during practice projection).

## Lifecycle & Memory Leak Prevention

- **Resource Ownership**: `FloatingWindow` owns the playback lifecycle via an `IAudioPlayer` abstraction (wrapping `System.Windows.Media.MediaPlayer`).
- **Teardown**:
  - `MediaPlayer` event handlers (`MediaEnded`, `MediaFailed`) are cleanly detached before reloading or upon window closing.
  - In-flight TTS downloads use `CancellationTokenSource`, cancelled on sentence change, window close, or explicit user stop.
  - `MediaPlayer` is stopped and closed when `FloatingWindow` unloads or closes.

## Verification

- **Core & Domain Tests**:
  - `TtsSettings` serialization, default values, and migration.
  - Cache key generation and cache eviction rules.
- **Service & Infrastructure Tests**:
  - `EdgeNeuralTtsService` SSML generation, rate formatting, and cancellation handling.
  - Mocked network failure and retry/timeout handling.
- **UI & Workflow Tests**:
  - Toggle play/stop transitions and state resets on sentence navigation.
  - Settings page voice and speed binding updates.
- **Integration & Build Verification**:
  - Full test suite passes (`dotnet test`).
  - Windows release build passes (`Publish-WindowsRelease.ps1`).
