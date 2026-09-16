using PteFloatingSentence.Core;
using PteFloatingSentence.Windows;
using PteFloatingSentence.Windows.Infrastructure;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class SettingsWorkflowTests
{
    [TestMethod]
    public void StudyListDraft_CreateList_UsesUniqueIdAndDefaultTarget()
    {
        var settings = AppSettings.Default;
        var draft = new StudyListDraft(settings, _ => { });

        var created = draft.CreateList();

        Assert.AreNotEqual(Guid.Empty, created.Id);
        Assert.AreNotEqual(settings.StudyLists.Single().Id, created.Id);
        Assert.AreEqual("New list", created.Name);
        Assert.AreEqual(10, created.TargetSentenceCount);
    }

    [TestMethod]
    public void StudyListDraft_DeleteLastList_ReturnsExpectedError()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });

        var result = draft.DeleteSelectedList();

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Cannot delete the last list.", result.Error);
    }

    [TestMethod]
    public void StudyListDraft_AddSentence_RejectsTwentyOneWords()
    {
        var draft = new StudyListDraft(AppSettings.Default, _ => { });
        var sentence = string.Join(' ', Enumerable.Repeat("word", 21));

        var result = draft.AddSentence(sentence);

        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("Use 20 words or fewer.", result.Error);
    }

    [TestMethod]
    public void StudyListDraft_SelectSentence_OnlyChangesSelectedListsCurrentIndex()
    {
        var first = new StudyList(Guid.NewGuid(), "First", 10, 0,
            [new StudySentence(Guid.NewGuid(), "First sentence."), new StudySentence(Guid.NewGuid(), "Second sentence.")]);
        var second = new StudyList(Guid.NewGuid(), "Second", 10, 0,
            [new StudySentence(Guid.NewGuid(), "Third sentence."), new StudySentence(Guid.NewGuid(), "Fourth sentence.")]);
        var draft = new StudyListDraft(new AppSettings { ActiveListId = first.Id, StudyLists = [first, second] }, _ => { });

        draft.SelectSentence(first.Sentences[1].Id);

        Assert.AreEqual(1, draft.Settings.StudyLists.Single(list => list.Id == first.Id).CurrentSentenceIndex);
        Assert.AreEqual(0, draft.Settings.StudyLists.Single(list => list.Id == second.Id).CurrentSentenceIndex);
    }

    [TestMethod]
    public void StudyListDraft_Cancel_DoesNotInvokeSaveCallback()
    {
        var callbackInvoked = false;
        var draft = new StudyListDraft(AppSettings.Default, _ => callbackInvoked = true);

        draft.Cancel();

        Assert.IsFalse(callbackInvoked);
    }

    [TestMethod]
    public void StudyListDraft_Save_InvokesCallbackOnlyOnce()
    {
        var callbackCount = 0;
        var draft = new StudyListDraft(AppSettings.Default, _ => callbackCount++);

        Assert.IsTrue(draft.Save().IsValid);
        Assert.IsTrue(draft.Save().IsValid);

        Assert.AreEqual(1, callbackCount);
    }

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
