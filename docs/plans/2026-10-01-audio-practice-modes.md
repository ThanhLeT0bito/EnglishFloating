# Audio Practice Modes and TTS Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development (recommended) or executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Make Windows Practice consistent in the floating widget and Settings, add text-hint and listen-and-write modes with audio in both, and fix the two TTS review findings.

**Architecture:** Core owns the saved mode and whole-sentence comparison. A WPF SentenceAudioPlayback class owns one request/player lifecycle; both practice hosts and the normal floating sentence use it. A shared PracticePanel control owns practice rendering and input behavior; FloatingWindow and ReviewPracticePage only host it and supply list/settings callbacks.

**Tech Stack:** .NET 9, WPF, xUnit/FluentAssertions, Microsoft Edge Neural TTS WebSocket, System.Windows.Media.MediaPlayer.

## Global Constraints

- Work on feature/sentence-audio-tts; do not modify the macOS worktree.
- Existing installations default to Text hints. Mode is saved app-wide, not per list.
- Listen & write shows no sentence text, punctuation, word count, token boxes, or answer-length clue before completion. Audio does not auto-play.
- Audio is visible in normal floating reading and in both practice modes on both hosts, disabled only for an empty list.
- Correct answers alone mark completion. On success, stop audio and advance; after the last sentence show completion. Do not offer Show answer.
- Cache limit is fixed at 20 MB. A TTS cache miss has a 20-second total timeout; no automatic retry.
- Run the focused failing test before production edits, then focused green tests, full Windows tests, and Release build.

## File structure

- Core/PracticeMode.cs: enum with stable serialized numeric values.
- Core/ReviewPractice.cs: whole-sentence comparison and session-independent normalization.
- Windows/ReviewPracticeSession.cs: applies whole-sentence result to existing completion state.
- Windows/Infrastructure/EdgeNeuralTtsService.cs: total timeout.
- Windows/Infrastructure/SentenceAudioPlayback.cs: cache, synthesis, play/stop, cancellation, and state/errors.
- Windows/PracticePanel.xaml(.cs): reusable compact/expanded practice interaction and audio button.
- Windows/FloatingWindow.xaml(.cs): hosts PracticePanel; normal audio button delegates to SentenceAudioPlayback.
- Windows/ReviewPracticePage.xaml(.cs): keeps list/mode selector and hosts the same PracticePanel.
- Core/AppSettings.cs, Core/SettingsUpdateMerger.cs, Windows/StudyListDraft.cs, Windows/SettingsWindow.xaml.cs: mode persistence; remove unused cache-limit field.

---

### Task 1: Bound TTS synthesis and remove misleading cache setting

**Files:**
- Modify: src/PteFloatingSentence.Windows/Infrastructure/EdgeNeuralTtsService.cs
- Modify: src/PteFloatingSentence.Core/AppSettings.cs
- Modify: src/PteFloatingSentence.Core/SettingsUpdateMerger.cs
- Modify: src/PteFloatingSentence.Windows/StudyListDraft.cs
- Modify: src/PteFloatingSentence.Windows/DisplayPage.xaml.cs
- Test: tests/PteFloatingSentence.Windows.Tests/EdgeNeuralTtsServiceTests.cs
- Test: tests/PteFloatingSentence.Windows.Tests/AudioCacheManagerTests.cs
- Test: tests/PteFloatingSentence.Windows.Tests/JsonSettingsStoreTests.cs
- Test: tests/PteFloatingSentence.Core.Tests/SettingsUpdateMergerTests.cs

**Interfaces:**
- Preserve ITtsService.SynthesizeSpeechAsync(string text, string voice, double speed, CancellationToken cancellationToken).
- TimeoutException means the 20-second internal limit; OperationCanceledException means caller cancellation.
- AudioCacheManager.DefaultMaxCacheSizeBytes is the one 20 MB source.

- [ ] **Step 1: Write failing tests.** Add a fake IWebSocketClient whose ConnectAsync or ReceiveAsync waits until its supplied token is cancelled. Assert the service ends with TimeoutException when the internal deadline expires, and OperationCanceledException when caller cancellation fires first. Make the deadline internal constructor-injected for tests so the test uses 20 ms rather than waiting 20 seconds. Add a Settings test showing old JSON with TtsMaxCacheSizeBytes loads while the new AppSettings has no such property. Assert DisplayPage's label uses AudioCacheManager.DefaultMaxCacheSizeBytes.

~~~csharp
var service = new EdgeNeuralTtsService(
    () => new BlockingWebSocketClient(),
    TimeSpan.FromMilliseconds(20));
var act = () => service.SynthesizeSpeechAsync("Test", "en-US-JennyNeural", 1.0);
await act.Should().ThrowAsync<TimeoutException>();
~~~

- [ ] **Step 2: Run red tests.** Run dotnet test tests/PteFloatingSentence.Windows.Tests -c Release --filter "FullyQualifiedName~EdgeNeuralTtsServiceTests|FullyQualifiedName~AudioCacheManagerTests" and dotnet test tests/PteFloatingSentence.Core.Tests -c Release --filter "FullyQualifiedName~SettingsUpdateMergerTests". Expect the new timeout-constructor and removed-property assertions to fail.
- [ ] **Step 3: Implement the bounded call.** Move the current SynthesizeSpeechAsync body to a private SynthesizeCoreAsync that receives the effective token. Wrap it as below; keep all WebSocket operations on the effective token, including connect, receive, and graceful close (replace the current CancellationToken.None close argument). Do not translate explicit caller cancellation.

~~~csharp
private readonly TimeSpan _timeout;

internal EdgeNeuralTtsService(
    Func<IWebSocketClient> webSocketFactory,
    TimeSpan? timeout = null)
{
    _webSocketFactory = webSocketFactory ?? throw new ArgumentNullException(nameof(webSocketFactory));
    _timeout = timeout ?? TimeSpan.FromSeconds(20);
}

public async Task<Stream> SynthesizeSpeechAsync(
    string text, string voice, double speed, CancellationToken cancellationToken = default)
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
~~~

- [ ] **Step 4: Delete TtsMaxCacheSizeBytes from AppSettings, SettingsUpdateMerger, and StudyListDraft.** DisplayPage uses AudioCacheManager.DefaultMaxCacheSizeBytes directly. Do not alter TtsVoice or TtsSpeed. Confirm JsonSettingsStore ignores the old JSON member without migrating user settings.

~~~csharp
public string TtsVoice { get; init; } = "en-US-JennyNeural";
public double TtsSpeed { get; init; } = 1.0;
// AppSettings has no TtsMaxCacheSizeBytes member.
var maxMb = AudioCacheManager.DefaultMaxCacheSizeBytes / (1024.0 * 1024.0);
~~~
- [ ] **Step 5: Run green tests and commit.** Run the two focused commands above plus dotnet test PteFloatingSentence.sln -c Release --nologo. Expect all pass. Commit only the listed files as fix(tts): bound synthesis and use one cache limit.

### Task 2: Save practice mode and grade a full dictated sentence

**Files:**
- Create: src/PteFloatingSentence.Core/PracticeMode.cs
- Modify: src/PteFloatingSentence.Core/AppSettings.cs
- Modify: src/PteFloatingSentence.Core/SettingsUpdateMerger.cs
- Modify: src/PteFloatingSentence.Core/ReviewPractice.cs
- Modify: src/PteFloatingSentence.Windows/StudyListDraft.cs
- Modify: src/PteFloatingSentence.Windows/ReviewPracticeSession.cs
- Test: tests/PteFloatingSentence.Core.Tests/ReviewPracticeTests.cs
- Test: tests/PteFloatingSentence.Core.Tests/SettingsUpdateMergerTests.cs
- Test: tests/PteFloatingSentence.Windows.Tests/ReviewPracticeSessionTests.cs

**Interfaces:**
- Produce PracticeMode.TextHints = 0 and PracticeMode.ListenAndWrite = 1.
- Produce ReviewPracticeRules.IsCorrectDictation(string expected, string? answer).
- Produce ReviewPracticeSession.SubmitDictation(string answer) returning ReviewAnswerResult.

- [ ] **Step 1: Write failing Core tests.** Cover case, repeated spaces, terminal punctuation, curly quotes, internal apostrophe, missing word, extra word, changed order, misspelling, and empty input. Assert old settings default to TextHints and SettingsUpdateMerger retains submitted mode.

~~~csharp
ReviewPracticeRules.IsCorrectDictation(
    "Don't stop at the station.",
    "don't   stop at the station").Should().BeTrue();
ReviewPracticeRules.IsCorrectDictation(
    "You must wear a hard hat.",
    "You must wear hard hat").Should().BeFalse();
AppSettings.Default.PracticeMode.Should().Be(PracticeMode.TextHints);
~~~

- [ ] **Step 2: Write failing session tests.** SubmitDictation with a wrong full sentence leaves CurrentHiddenPosition and IsComplete unchanged and does not invoke completion callback. Correct input invokes callback once, sets IsComplete, and updates IsAllSentencesCompleted on the last sentence.
- [ ] **Step 3: Run red tests.** Run dotnet test tests/PteFloatingSentence.Core.Tests -c Release --filter "FullyQualifiedName~ReviewPracticeTests|FullyQualifiedName~SettingsUpdateMergerTests" and dotnet test tests/PteFloatingSentence.Windows.Tests -c Release --filter "FullyQualifiedName~ReviewPracticeSessionTests". Expect missing enum/method failures.
- [ ] **Step 4: Implement enum and normalization.** Add the enum and AppSettings property, merge it, and copy it in StudyListDraft.UpdateSettingsFromApp. Add StudyListDraft.SetPracticeMode(PracticeMode mode). Split expected and answer on whitespace, trim only leading/trailing punctuation (including curly quotes) from each token, discard resulting empty tokens, join with one space, and compare OrdinalIgnoreCase. Preserve apostrophes inside tokens.

~~~csharp
public enum PracticeMode { TextHints = 0, ListenAndWrite = 1 }

public static bool IsCorrectDictation(string expected, string? answer)
{
    static string Normalize(string? text) => string.Join(
        " ",
        (text ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim().Trim(TrimPunctuationChars))
            .Where(word => word.Length > 0));
    var normalizedExpected = Normalize(expected);
    return normalizedExpected.Length > 0
        && string.Equals(normalizedExpected, Normalize(answer), StringComparison.OrdinalIgnoreCase);
}
~~~

- [ ] **Step 5: Implement SubmitDictation using the session's existing completion callback.** Extract the current successful-completion body in Submit to CompleteCurrentSentence(), call it from both Submit and SubmitDictation, and return ReviewAnswerResult(false,false,CurrentHiddenPosition,"Try again.") for an incorrect full sentence. Do not increment hidden-word position in dictation mode.

~~~csharp
public ReviewAnswerResult SubmitDictation(string answer)
{
    if (List.Sentences.Count == 0 || IsComplete)
        return new ReviewAnswerResult(false, IsComplete, CurrentHiddenPosition, "Try again.");
    if (!ReviewPracticeRules.IsCorrectDictation(CurrentReview.OriginalText, answer))
        return new ReviewAnswerResult(false, false, CurrentHiddenPosition, "Try again.");
    CompleteCurrentSentence();
    return new ReviewAnswerResult(true, true, CurrentHiddenPosition, null);
}
~~~
- [ ] **Step 6: Run green tests and commit.** Run focused tests, then both Core and Windows test projects in Release. Expect all pass. Commit as feat(practice): persist mode and grade dictated sentences.

### Task 3: Centralize audio lifecycle for normal and practice surfaces

**Files:**
- Create: src/PteFloatingSentence.Windows/Infrastructure/SentenceAudioPlayback.cs
- Modify: src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs
- Test: tests/PteFloatingSentence.Windows.Tests/SentenceAudioPlaybackTests.cs
- Test: tests/PteFloatingSentence.Windows.Tests/FloatingAudioWorkflowTests.cs

**Interfaces:**
- Produce SentenceAudioPlayback(IAudioPlayer player, ITtsService tts, IAudioCacheManager cache).
- Produce Task ToggleAsync(string text, string voice, double speed), Stop(), Dispose(), StateChanged event.
- State is AudioPlaybackState(bool IsLoading, bool IsPlaying, string? Error).

- [ ] **Step 1: Write failing workflow tests.** Use the existing fake player/TTS/cache patterns in FloatingAudioWorkflowTests. Assert cache hit skips TTS, cache miss saves then plays, second click stops, navigation Stop cancels pending request, late TTS completion cannot start playback, TimeoutException becomes a retryable error, and Dispose stops/disposes the player. Verify failed attempts do not remain loading.

~~~csharp
var workflow = new SentenceAudioPlayback(player, tts, cache);
await workflow.ToggleAsync("A short sentence.", "en-US-JennyNeural", 1.0);
tts.CallCount.Should().Be(1);
player.PlayCount.Should().Be(1);
workflow.State.IsLoading.Should().BeFalse();
~~~

- [ ] **Step 2: Run red tests.** Run dotnet test tests/PteFloatingSentence.Windows.Tests -c Release --filter "FullyQualifiedName~SentenceAudioPlaybackTests". Expect missing type errors.
- [ ] **Step 3: Implement the lifecycle.** Keep a generation counter and CancellationTokenSource. Toggle while loading/playing calls Stop. A new toggle checks cache, then awaits TTS and cache save with the active token. Check generation/token before play; callbacks only update the same generation. Stop cancels, stops the player, and resets State. Catch TimeoutException and other non-cancellation exceptions into State.Error. Dispose cancels, detaches callbacks, and disposes the player.

~~~csharp
public sealed record AudioPlaybackState(bool IsLoading, bool IsPlaying, string? Error);

public interface ISentenceAudioPlayback : IDisposable
{
    AudioPlaybackState State { get; }
    event Action<AudioPlaybackState>? StateChanged;
    Task ToggleAsync(string text, string voice, double speed);
    void Stop();
}

~~~

- [ ] **Step 4: Replace FloatingWindow's duplicated audio state machine with the workflow.** Preserve its public injected AudioPlayer/TtsService/AudioCacheManager seams so existing UI tests continue working. UI StateChanged handler marshals to Dispatcher before updating the icon/tooltip. Keep StopAndResetAudio delegating to workflow.Stop. Sentence/list change, hide, close, and entering/exiting practice call Stop.
- [ ] **Step 5: Run green tests and commit.** Run SentenceAudioPlaybackTests and FloatingAudioWorkflowTests, then all Windows tests. Expect all pass. Commit as refactor(tts): share playback lifecycle.

### Task 4: Build one reusable PracticePanel

**Files:**
- Create: src/PteFloatingSentence.Windows/PracticePanel.xaml
- Create: src/PteFloatingSentence.Windows/PracticePanel.xaml.cs
- Test: tests/PteFloatingSentence.Windows.Tests/PracticePanelTests.cs

**Interfaces:**
- Produce PracticePanel.Load(ReviewPracticeSession session, AppSettings settings, ISentenceAudioPlayback audio).
- Produce PracticePanel.UpdateMode(PracticeMode mode) and SentenceChanged event.
- The panel hosts its own progress, speaker, projection/input, feedback, Previous/Next/Restart, and completion state.

- [ ] **Step 1: Write failing WPF tests.** Construct a panel on an STA test thread using the existing WPF test helper. In TextHints mode assert visible words and hidden boxes. In ListenAndWrite mode assert zero answer TextBlocks/hidden boxes, a single editable full-sentence TextBox, and a visible audio button. Assert wrong answer never calls completion callback, correct answer advances, mode switch clears text, and navigation stops audio.

~~~csharp
panel.Load(session, settings with { PracticeMode = PracticeMode.ListenAndWrite }, audio);
panel.FindName("PracticeAudioButton").Should().NotBeNull();
panel.FindName("DictationInput").Should().BeOfType<TextBox>();
((WrapPanel)panel.FindName("PracticeProjectionPanel"))
    .Children.OfType<TextBlock>().Should().BeEmpty();
~~~

- [ ] **Step 2: Run red tests.** Run dotnet test tests/PteFloatingSentence.Windows.Tests -c Release --filter "FullyQualifiedName~PracticePanelTests". Expect missing control/API failures.
- [ ] **Step 3: Create PracticePanel.xaml.** Place a compact header with progress + speaker, a content area with a WrapPanel for TextHints and a single TextBox for ListenAndWrite, generic inline feedback, Previous/Next, and Restart/completion. The floating host keeps its existing Exit button. The speaker remains visible for every non-empty current sentence. No Show answer button. Give the control a Compact property that changes margins/font sizes, not behavior.

~~~xml
<StackPanel>
  <Grid>
    <TextBlock x:Name="PracticeProgressLabel" />
    <Button x:Name="PracticeAudioButton" Content="&#xE767;"
            FontFamily="Segoe MDL2 Assets" Click="PracticeAudioButton_Click" />
  </Grid>
  <WrapPanel x:Name="PracticeProjectionPanel" />
  <TextBox x:Name="DictationInput" Visibility="Collapsed"
           KeyDown="DictationInput_KeyDown" />
  <TextBlock x:Name="PracticeFeedbackLabel" Visibility="Collapsed" />
  <StackPanel Orientation="Horizontal">
    <Button x:Name="PracticePreviousButton" Content="Previous" Click="Previous_Click" />
    <Button x:Name="PracticeNextButton" Content="Next" Click="Next_Click" />
    <Button x:Name="PracticeCheckButton" Content="Check answer" Click="Check_Click" />
  </StackPanel>
  <Border x:Name="PracticeCompletionPanel" Visibility="Collapsed">
    <Button x:Name="PracticeRestartButton" Content="Restart" Click="Restart_Click" />
  </Border>
</StackPanel>
~~~
- [ ] **Step 4: Implement PracticePanel.xaml.cs.** Render from ReviewPracticeSession.CurrentReview; TextHints submits via session.Submit (preserve Enter and Space submission), ListenAndWrite submits via session.SubmitDictation (Enter or Check button). Correct completion stops audio and calls MoveNextSentence unless all sentences complete. A mode change, list change, navigation, restart, or unload stops audio and clears input. The speaker calls audio.ToggleAsync with the session's current sentence text and saved TtsVoice/TtsSpeed. Use the same generic feedback/error copy in both layouts.

~~~csharp
private async void PracticeAudioButton_Click(object sender, RoutedEventArgs e)
{
    if (_session is null || _session.List.Sentences.Count == 0 || _audio is null)
        return;
    var sentence = _session.List.Sentences[_session.SentenceIndex];
    await _audio.ToggleAsync(sentence.Text, _settings.TtsVoice, _settings.TtsSpeed);
}

private void SubmitDictation()
{
    if (_session is null) return;
    var result = _session.SubmitDictation(DictationInput.Text);
    if (result.IsCorrect)
        AdvanceOrComplete();
    else
        PracticeFeedbackLabel.Text = "Try again.";
}
~~~
- [ ] **Step 5: Run green tests and commit.** Run PracticePanelTests plus current ReviewPracticeSessionTests. Expect all pass. Commit as feat(practice): create shared audio-capable practice panel.

### Task 5: Host the same panel in floating and Settings Practice

**Files:**
- Modify: src/PteFloatingSentence.Windows/FloatingWindow.xaml
- Modify: src/PteFloatingSentence.Windows/FloatingWindow.xaml.cs
- Modify: src/PteFloatingSentence.Windows/ReviewPracticePage.xaml
- Modify: src/PteFloatingSentence.Windows/ReviewPracticePage.xaml.cs
- Modify: src/PteFloatingSentence.Windows/SettingsWindow.xaml.cs
- Test: tests/PteFloatingSentence.Windows.Tests/SettingsWorkflowTests.cs
- Test: tests/PteFloatingSentence.Windows.Tests/PracticeWorkflowTests.cs
- Test: tests/PteFloatingSentence.Windows.Tests/FloatingAudioWorkflowTests.cs

**Interfaces:**
- FloatingWindow.StartPractice(Guid? listId) remains public.
- ReviewPracticePage.Initialize(AppSettings settings, Action<Guid,Guid,bool>? onSentenceCompleted, int? seed) remains public.
- PracticePanel events send completed sentence IDs through each host's existing callback.
- ReviewPracticePage exposes event Action<PracticeMode> PracticeModeChanged; SettingsWindow handles it with StudyListDraft.SetPracticeMode.

- [ ] **Step 1: Write failing host tests.** Assert FloatingWindow practice contains PracticePanel and uses selected list (including a list different from active). Assert Settings Practice contains the same panel type, defaults to TextHints, saves mode selection, and matches floating auto-advance. Assert both display the speaker in both modes. Assert changing list/mode stops audio and does not leak prior sentence text.
- [ ] **Step 2: Run red tests.** Run dotnet test tests/PteFloatingSentence.Windows.Tests -c Release --filter "FullyQualifiedName~SettingsWorkflowTests|FullyQualifiedName~PracticeWorkflowTests|FullyQualifiedName~FloatingAudioWorkflowTests". Expect newly added assertions to fail.
- [ ] **Step 3: Replace the duplicated practice projection in FloatingWindow.** Keep outer floating chrome and navigation; embed PracticePanel in PracticeContainer with Compact=true, which hides the panel's internal Previous/Next buttons. Route outer Previous/Next buttons to panel navigation and preserve PracticeModeChanged/SentenceCompleted events. Remove old floating practice textbox handlers and projection code after the tests exercise the shared panel.

~~~xml
<StackPanel x:Name="PracticeContainer" Visibility="Collapsed">
  <local:PracticePanel x:Name="FloatingPracticePanel" />
</StackPanel>
~~~
- [ ] **Step 4: Replace Settings Practice's separate projection with PracticePanel.** Retain StudyListSelector above it and add two mode choices Text hints/Listen & write. The selector raises PracticeModeChanged; SettingsWindow handles it with StudyListDraft.SetPracticeMode immediately for preview, then Save persists it. A newly selected list loads a new ReviewPracticeSession into the panel. Remove the old Show answer button and old input handlers.

~~~xml
<StackPanel>
  <ComboBox x:Name="StudyListSelector" SelectionChanged="StudyListSelector_SelectionChanged" />
  <ComboBox x:Name="PracticeModeSelector" SelectionChanged="PracticeModeSelector_SelectionChanged">
    <ComboBoxItem Content="Text hints" Tag="TextHints" />
    <ComboBoxItem Content="Listen &amp; write" Tag="ListenAndWrite" />
  </ComboBox>
  <local:PracticePanel x:Name="SettingsPracticePanel" />
</StackPanel>
~~~
- [ ] **Step 5: Refresh settings header and cleanup.** SettingsWindow Practice subtitle names both modes. Dispose/unload handlers detach audio and WPF events. Confirm manual Previous/Next, Restart, empty list, completed list, and returning from Settings work on both hosts.
- [ ] **Step 6: Run green tests and commit.** Run focused host tests and all Windows tests. Expect all pass. Commit as feat(practice): unify floating and Settings modes.

### Task 6: Final verification and branch review

**Files:**
- Update: docs/specs/2026-10-01-audio-practice-modes-design.md only if an approved behavior changed during implementation.
- Update: docs/plans/2026-10-01-audio-practice-modes.md checkbox progress.

**Interfaces:** No new API.

- [ ] **Step 1: Run dotnet test PteFloatingSentence.sln -c Release --nologo.** Expect zero failed tests, including the live endpoint test. If the external endpoint fails, report that separately; do not hide or remove the test.
- [ ] **Step 2: Run dotnet build PteFloatingSentence.sln -c Release --nologo and the repository's Windows release publish script.** Expect exit code 0 and a fresh exe.
- [ ] **Step 3: Manual smoke test on Windows.** Verify normal speaker; Text hints and Listen & write in both floating and Settings; error tooltip; replay/stop; next/previous; switching list/mode; hide overlay; no answer text in audio-only mode; completion only after correct input.
- [ ] **Step 4: Review diff against spec.** Check git diff main...HEAD --check, inspect every changed file, and request code review per requesting-code-review skill. Fix any critical/important findings and rerun relevant tests.
- [ ] **Step 5: Report exact verification and artifact path.** Do not merge to main unless the user asks.
