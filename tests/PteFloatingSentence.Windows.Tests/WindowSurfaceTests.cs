using PteFloatingSentence.Core;
using System.Text.RegularExpressions;

namespace PteFloatingSentence.Windows.Tests;

[TestClass]
public class WindowSurfaceTests
{
    [TestMethod]
    public void FloatingWindow_ProvidesApplySettingsApi()
    {
        var floatingWindowType = typeof(App).Assembly.GetType("PteFloatingSentence.Windows.FloatingWindow");

        Assert.IsNotNull(floatingWindowType);
        Assert.IsNotNull(floatingWindowType.GetMethod("ApplySettings", [typeof(AppSettings)]));
    }

    [TestMethod]
    public void FloatingWindow_ProvidesPreviousAndNextNavigationEvents()
    {
        var floatingWindowType = typeof(App).Assembly.GetType("PteFloatingSentence.Windows.FloatingWindow");

        Assert.IsNotNull(floatingWindowType);
        Assert.IsNotNull(floatingWindowType.GetEvent("PreviousRequested"));
        Assert.IsNotNull(floatingWindowType.GetEvent("NextRequested"));
    }

    [TestMethod]
    public void FloatingWindowXaml_UsesHiddenNavigationButtonsAndPolishedSurface()
    {
        var xaml = File.ReadAllText(FindWorkspaceFile("src", "PteFloatingSentence.Windows", "FloatingWindow.xaml"));

        StringAssert.Contains(xaml, "x:Name=\"PreviousButton\"");
        StringAssert.Contains(xaml, "x:Name=\"NextButton\"");
        Assert.AreEqual(2, Regex.Matches(xaml, "Visibility=\"Collapsed\"").Count);
        StringAssert.Contains(xaml, "LinearGradientBrush");
        StringAssert.Contains(xaml, "Header=\"Settings\"");
        StringAssert.Contains(xaml, "Header=\"Exit\"");
        Assert.AreEqual(2, Regex.Matches(xaml, "<MenuItem\\s").Count);
    }

    [TestMethod]
    public void StudyListNavigation_WrapsWithoutChangingCompletion()
    {
        var first = new StudySentence(Guid.NewGuid(), "First.", true);
        var second = new StudySentence(Guid.NewGuid(), "Second.");
        var list = new StudyList(Guid.NewGuid(), "Practice", 10, 0, [first, second]);
        var settings = new AppSettings { ActiveListId = list.Id, StudyLists = [list] };

        var moved = StudyListRules.MoveCurrentSentence(settings, -1);
        var active = StudyListRules.ActiveList(moved);

        Assert.AreEqual(1, active.CurrentSentenceIndex);
        CollectionAssert.AreEqual(new[] { true, false }, active.Sentences.Select(sentence => sentence.IsCompleted).ToArray());
    }

    private static string FindWorkspaceFile(params string[] segments)
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
        }

        Assert.Fail($"Could not locate workspace file: {Path.Combine(segments)}");
        return string.Empty;
    }
}
