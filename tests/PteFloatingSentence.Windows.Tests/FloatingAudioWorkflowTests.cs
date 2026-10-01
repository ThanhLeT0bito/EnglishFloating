using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using PteFloatingSentence.Core;
using PteFloatingSentence.Windows;
using PteFloatingSentence.Windows.Infrastructure;
using Xunit;

namespace PteFloatingSentence.Windows.Tests;

public class FloatingAudioWorkflowTests
{
    [Fact]
    public void NavigatingToNextSentence_StopsActiveAudioPlayback()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer { IsPlaying = true };
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            var nextButton = (Button)window.FindName("NextButton");
            nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            fakePlayer.StopCallCount.Should().BeGreaterThan(0);
            fakePlayer.IsPlaying.Should().BeFalse();

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.Content.Should().Be("\uE767");

            window.Close();
        });
    }

    [Fact]
    public void NavigatingToPreviousSentence_StopsActiveAudioPlayback()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer { IsPlaying = true };
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            var prevButton = (Button)window.FindName("PreviousButton");
            prevButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            fakePlayer.StopCallCount.Should().BeGreaterThan(0);
            fakePlayer.IsPlaying.Should().BeFalse();

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.Content.Should().Be("\uE767");

            window.Close();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangingListOrCurrentTextWithSameSentenceId_StopsPlayingAudio(bool changeList)
    {
        RunOnSta(() =>
        {
            var player = new FakeAudioPlayer();
            var cache = new FakeAudioCacheManager();
            cache.CachedFiles["cached.mp3"] = "First sentence to study.";
            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(player, new FakeTtsService(), cache);
            window.ApplySettings(settings);

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            player.IsPlaying.Should().BeTrue();

            window.ApplySettings(ChangeListOrText(settings, changeList));

            player.IsPlaying.Should().BeFalse();
            player.StopCallCount.Should().Be(1);
            audioButton.Content.Should().Be("\uE767");
            window.Close();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangingListOrCurrentTextWithSameSentenceId_CancelsLoadingAudio(bool changeList)
    {
        RunOnSta(() =>
        {
            var player = new FakeAudioPlayer();
            var tts = new FakeTtsService { Pending = true };
            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(player, tts, new FakeAudioCacheManager());
            window.ApplySettings(settings);

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tts.SynthesizeCallCount.Should().Be(1);
            audioButton.ToolTip.Should().Be("Loading audio...");

            window.ApplySettings(ChangeListOrText(settings, changeList));

            tts.LastToken.IsCancellationRequested.Should().BeTrue();
            audioButton.Content.Should().Be("\uE767");
            player.PlayCallCount.Should().Be(0);
            tts.CompletePending();
            DispatcherHelper.DoEvents();
            player.PlayCallCount.Should().Be(0);
            window.Close();
        });
    }

    [Fact]
    public void UnrelatedSettingsUpdate_KeepsCurrentAudioPlaying()
    {
        RunOnSta(() =>
        {
            var player = new FakeAudioPlayer();
            var cache = new FakeAudioCacheManager();
            cache.CachedFiles["cached.mp3"] = "First sentence to study.";
            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(player, new FakeTtsService(), cache);
            window.ApplySettings(settings);
            ((Button)window.FindName("AudioButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            window.ApplySettings(settings with { FontSize = settings.FontSize + 1 });

            player.IsPlaying.Should().BeTrue();
            player.StopCallCount.Should().Be(0);
            window.Close();
        });
    }

    [Fact]
    public void StartingPracticeMode_StopsActiveAudioPlayback()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer { IsPlaying = true };
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            window.StartPractice();

            fakePlayer.StopCallCount.Should().BeGreaterThan(0);
            fakePlayer.IsPlaying.Should().BeFalse();

            window.Close();
        });
    }

    [Fact]
    public void PracticeAudioButton_PlaysAudio_AndNavigatingOrExitingStopsIt()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer();
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();
            fakeCache.CachedFiles["cached.mp3"] = "First sentence to study.";

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);
            window.StartPractice();

            var practiceAudioButton = (Button)window.FindName("PracticeAudioButton");
            practiceAudioButton.Visibility.Should().Be(Visibility.Visible);
            practiceAudioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            fakePlayer.IsPlaying.Should().BeTrue();

            var nextButton = (Button)window.FindName("NextButton");
            nextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            fakePlayer.IsPlaying.Should().BeFalse();

            practiceAudioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            fakePlayer.IsPlaying.Should().BeTrue();

            window.ExitPractice();
            fakePlayer.IsPlaying.Should().BeFalse();

            window.Close();
        });
    }

    [Fact]
    public void ClosingWindow_StopsAndDisposesAudioPlayer()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer { IsPlaying = true };
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            window.Close();

            fakePlayer.StopCallCount.Should().BeGreaterThan(0);
            fakePlayer.DisposeCallCount.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void ReassigningSameAudioPlayer_DoesNotDisposeItBeforeWindowCloses()
    {
        RunOnSta(() =>
        {
            var player = new FakeAudioPlayer();
            var window = new FloatingWindow(player, new FakeTtsService(), new FakeAudioCacheManager());

            window.AudioPlayer = player;

            player.DisposeCallCount.Should().Be(0);
            window.Close();
            player.DisposeCallCount.Should().Be(1);
        });
    }

    [Fact]
    public void AudioButtonClick_PlaysCachedAudioImmediately_WhenCacheHits()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer();
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();
            fakeCache.CachedFiles["cached.mp3"] = "First sentence to study.";

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            fakeTts.SynthesizeCallCount.Should().Be(0);
            fakePlayer.PlayCallCount.Should().Be(1);
            fakePlayer.LastPlayedPath.Should().Be("cached.mp3");

            window.Close();
        });
    }

    [Fact]
    public void AudioButtonClick_TogglesStop_WhenAlreadyPlaying()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer();
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();
            fakeCache.CachedFiles["cached.mp3"] = "First sentence to study.";

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            var audioButton = (Button)window.FindName("AudioButton");
            // First click: starts play
            audioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            fakePlayer.IsPlaying.Should().BeTrue();

            // Second click: stops play
            audioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            fakePlayer.StopCallCount.Should().Be(1);
            fakePlayer.IsPlaying.Should().BeFalse();
            audioButton.Content.Should().Be("\uE767");

            window.Close();
        });
    }

    [Fact]
    public void AudioPlayback_WhenMediaEnded_ResetsAudioButtonToIdle()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer();
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();
            fakeCache.CachedFiles["cached.mp3"] = "First sentence to study.";

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            audioButton.Content.Should().Be("\uE768");

            // Simulate playback finished
            fakePlayer.TriggerMediaEnded();

            // Run pending dispatcher actions
            DispatcherHelper.DoEvents();

            audioButton.Content.Should().Be("\uE767");
            fakePlayer.IsPlaying.Should().BeFalse();

            window.Close();
        });
    }

    [Fact]
    public void AudioPlayback_WhenMediaFailed_ResetsAudioButtonToIdleWithoutCrashing()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer();
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();
            fakeCache.CachedFiles["cached.mp3"] = "First sentence to study.";

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // Simulate media failed
            fakePlayer.TriggerMediaFailed(new IOException("Audio file corrupted"));

            DispatcherHelper.DoEvents();

            audioButton.Content.Should().Be("\uE767");
            fakePlayer.IsPlaying.Should().BeFalse();

            window.Close();
        });
    }

    [Fact]
    public void AudioButtonClick_SynthesizesAndCaches_WhenCacheMisses()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer();
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            fakeTts.SynthesizeCallCount.Should().Be(1);
            fakeCache.SaveCallCount.Should().Be(1);
            fakePlayer.PlayCallCount.Should().Be(1);
            fakePlayer.LastPlayedPath.Should().Be("cached.mp3");

            window.Close();
        });
    }

    [Fact]
    public void AudioButton_DisabledWhenSentenceEmptyOrNull()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer();
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();

            var listId = Guid.NewGuid();
            var list = new StudyList(listId, "Empty List", 10, 0, []);
            var settings = AppSettings.Default with
            {
                ActiveListId = listId,
                StudyLists = [list]
            };

            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);

            var audioButton = (Button)window.FindName("AudioButton");
            audioButton.IsEnabled.Should().BeFalse();

            window.Close();
        });
    }

    [Fact]
    public void HidingWindow_CancelsAndStopsAudioPlayback()
    {
        RunOnSta(() =>
        {
            var fakePlayer = new FakeAudioPlayer { IsPlaying = true };
            var fakeTts = new FakeTtsService();
            var fakeCache = new FakeAudioCacheManager();

            var settings = CreateTwoSentenceSettings();
            var window = new FloatingWindow(fakePlayer, fakeTts, fakeCache);
            window.ApplySettings(settings);
            window.Show();

            window.Visibility = Visibility.Collapsed;
            DispatcherHelper.DoEvents();

            fakePlayer.StopCallCount.Should().BeGreaterThan(0);
            fakePlayer.IsPlaying.Should().BeFalse();

            window.Close();
        });
    }

    [Fact]
    public void WpfAudioPlayer_Lifecycle_DetachesEventsAndStopsOnDispose()
    {
        RunOnSta(() =>
        {
            var player = new WpfAudioPlayer();
            player.IsPlaying.Should().BeFalse();

            player.Stop();
            player.IsPlaying.Should().BeFalse();

            player.Dispose();

            var act = () => player.PlayFileAsync("dummy.mp3", () => { }, _ => { });
            act.Should().ThrowAsync<ObjectDisposedException>();
        });
    }

    private static AppSettings CreateTwoSentenceSettings()
    {
        var listId = Guid.NewGuid();
        var s1 = new StudySentence(Guid.NewGuid(), "First sentence to study.");
        var s2 = new StudySentence(Guid.NewGuid(), "Second sentence to study.");
        var list = new StudyList(listId, "Default List", 10, 0, [s1, s2]);

        return AppSettings.Default with
        {
            ActiveListId = listId,
            StudyLists = [list]
        };
    }

    private static AppSettings ChangeListOrText(AppSettings settings, bool changeList)
    {
        var originalList = settings.StudyLists.Single();
        if (changeList)
        {
            var secondList = originalList with { Id = Guid.NewGuid(), Name = "Second list" };
            return settings with
            {
                ActiveListId = secondList.Id,
                StudyLists = [secondList]
            };
        }

        var editedSentence = originalList.Sentences[0] with { Text = "Edited sentence text." };
        var editedList = originalList with { Sentences = [editedSentence, originalList.Sentences[1]] };
        return settings with { StudyLists = [editedList] };
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public bool IsPlaying { get; set; }
        public int PlayCallCount { get; private set; }
        public int StopCallCount { get; private set; }
        public int DisposeCallCount { get; private set; }
        public string? LastPlayedPath { get; private set; }
        public Action? LastOnEnded { get; private set; }
        public Action<Exception>? LastOnError { get; private set; }

        public Task PlayFileAsync(string filePath, Action onEnded, Action<Exception> onError)
        {
            PlayCallCount++;
            LastPlayedPath = filePath;
            LastOnEnded = onEnded;
            LastOnError = onError;
            IsPlaying = true;
            return Task.CompletedTask;
        }

        public void Stop()
        {
            StopCallCount++;
            IsPlaying = false;
        }

        public void TriggerMediaEnded()
        {
            IsPlaying = false;
            LastOnEnded?.Invoke();
        }

        public void TriggerMediaFailed(Exception ex)
        {
            IsPlaying = false;
            LastOnError?.Invoke(ex);
        }

        public void Dispose()
        {
            DisposeCallCount++;
            Stop();
        }
    }

    private sealed class FakeTtsService : ITtsService
    {
        public int SynthesizeCallCount { get; private set; }
        public bool Pending { get; set; }
        public CancellationToken LastToken { get; private set; }
        private TaskCompletionSource<Stream>? _completion;

        public Task<Stream> SynthesizeSpeechAsync(string text, string voice, double speed, CancellationToken cancellationToken = default)
        {
            SynthesizeCallCount++;
            LastToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            if (Pending)
            {
                _completion = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
                return _completion.Task;
            }
            var memory = new MemoryStream(Encoding.UTF8.GetBytes("fake audio bytes"));
            return Task.FromResult<Stream>(memory);
        }

        public void CompletePending() => _completion!.SetResult(new MemoryStream(Encoding.UTF8.GetBytes("late audio")));
    }

    private sealed class FakeAudioCacheManager : IAudioCacheManager
    {
        public Dictionary<string, string> CachedFiles { get; } = new();
        public int SaveCallCount { get; private set; }

        public string GetCacheFilePath(string text, string voice, double speed) => "cached.mp3";

        public bool TryGetCachedAudio(string text, string voice, double speed, out string filePath)
        {
            if (CachedFiles.ContainsKey("cached.mp3"))
            {
                filePath = "cached.mp3";
                return true;
            }

            filePath = string.Empty;
            return false;
        }

        public Task SaveAudioAsync(string text, string voice, double speed, Stream audioStream, CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            CachedFiles["cached.mp3"] = text;
            return Task.CompletedTask;
        }

        public long GetTotalCacheSizeBytes() => 0;

        public void ClearCache() => CachedFiles.Clear();

        public void PruneToLimit(long maxSizeBytes) { }
    }

    private static class DispatcherHelper
    {
        public static void DoEvents()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new System.Windows.Threading.DispatcherOperationCallback(ExitFrame),
                frame);
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static object? ExitFrame(object state)
        {
            ((System.Windows.Threading.DispatcherFrame)state).Continue = false;
            return null;
        }
    }
}
