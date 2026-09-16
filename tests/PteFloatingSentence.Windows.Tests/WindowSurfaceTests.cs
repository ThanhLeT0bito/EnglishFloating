using PteFloatingSentence.Core;

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
}
