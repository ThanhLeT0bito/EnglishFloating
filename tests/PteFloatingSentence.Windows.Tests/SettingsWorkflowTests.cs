using PteFloatingSentence.Core;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class SettingsWorkflowTests
{
    [TestMethod]
    public async Task SettingsPersistenceQueue_AllowsALaterSaveAfterAFailedWrite()
    {
        var queueType = typeof(JsonSettingsStore).Assembly.GetType("PteFloatingSentence.Windows.Infrastructure.SettingsPersistenceQueue");

        Assert.IsNotNull(queueType);
        var queueMethod = queueType.GetMethod("Queue");
        var flushMethod = queueType.GetMethod("FlushAsync");
        Assert.IsNotNull(queueMethod);
        Assert.IsNotNull(flushMethod);

        var writes = new List<AppSettings>();
        var failFirstWrite = true;
        Func<AppSettings, Task> save = settings =>
        {
            writes.Add(settings);
            if (failFirstWrite)
            {
                failFirstWrite = false;
                throw new IOException("Settings are temporarily unavailable.");
            }

            return Task.CompletedTask;
        };
        var queue = Activator.CreateInstance(queueType, save)!;
        var first = AppSettings.Default with { Sentence = "First" };
        var retry = AppSettings.Default with { Sentence = "Retry" };

        queueMethod.Invoke(queue, [first]);
        await (Task)flushMethod.Invoke(queue, null)!;
        queueMethod.Invoke(queue, [retry]);
        await (Task)flushMethod.Invoke(queue, null)!;

        CollectionAssert.AreEqual(new[] { first, retry }, writes);
    }

    [TestMethod]
    public async Task SettingsPersistenceQueue_SerializesWritesAndCoalescesLatestSnapshot()
    {
        var queueType = typeof(JsonSettingsStore).Assembly.GetType("PteFloatingSentence.Windows.Infrastructure.SettingsPersistenceQueue");

        Assert.IsNotNull(queueType);
        var queueMethod = queueType.GetMethod("Queue");
        var flushMethod = queueType.GetMethod("FlushAsync");
        Assert.IsNotNull(queueMethod);
        Assert.IsNotNull(flushMethod);

        var writes = new List<AppSettings>();
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<AppSettings, Task> save = async settings =>
        {
            writes.Add(settings);
            if (writes.Count == 1)
            {
                firstWriteStarted.SetResult();
                await releaseFirstWrite.Task;
            }
        };
        var queue = Activator.CreateInstance(queueType, save)!;
        var first = AppSettings.Default with { Sentence = "First" };
        var second = AppSettings.Default with { Sentence = "Second" };
        var latest = AppSettings.Default with { Sentence = "Latest" };

        queueMethod.Invoke(queue, [first]);
        await firstWriteStarted.Task;
        queueMethod.Invoke(queue, [second]);
        queueMethod.Invoke(queue, [latest]);
        releaseFirstWrite.SetResult();
        await (Task)flushMethod.Invoke(queue, null)!;

        CollectionAssert.AreEqual(new[] { first, latest }, writes);
    }

    [TestMethod]
    public void SettingsNumericValidator_RejectsNonFiniteInputs()
    {
        var validatorType = typeof(AppSettings).Assembly.GetType("PteFloatingSentence.Core.SettingsNumericValidator");

        Assert.IsNotNull(validatorType);
        var fontSizeMethod = validatorType.GetMethod("IsValidFontSize");
        var opacityMethod = validatorType.GetMethod("IsValidOpacity");
        Assert.IsNotNull(fontSizeMethod);
        Assert.IsNotNull(opacityMethod);

        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.IsFalse((bool)fontSizeMethod.Invoke(null, [value])!);
            Assert.IsFalse((bool)opacityMethod.Invoke(null, [value])!);
        }
    }

    [TestMethod]
    public void SettingsUpdateMerger_PreservesLatestPositionAndVersion()
    {
        var mergerType = typeof(AppSettings).Assembly.GetType("PteFloatingSentence.Core.SettingsUpdateMerger");

        Assert.IsNotNull(mergerType);
        var mergeMethod = mergerType.GetMethod("MergeEditableFields");
        Assert.IsNotNull(mergeMethod);

        var latest = AppSettings.Default with { Version = 4, Left = 345, Top = 678, Sentence = "Original" };
        var submitted = AppSettings.Default with
        {
            Version = 1,
            Left = 100,
            Top = 200,
            Sentence = "Updated sentence",
            FontSize = 44,
            TextColor = "#FF102030",
            BackgroundOpacity = 0.7
        };

        var merged = (AppSettings)mergeMethod.Invoke(null, [latest, submitted])!;

        Assert.AreEqual(4, merged.Version);
        Assert.AreEqual(345d, merged.Left);
        Assert.AreEqual(678d, merged.Top);
        Assert.AreEqual("Updated sentence", merged.Sentence);
        Assert.AreEqual(44d, merged.FontSize);
        Assert.AreEqual("#FF102030", merged.TextColor);
        Assert.AreEqual(0.7d, merged.BackgroundOpacity);
    }
}
