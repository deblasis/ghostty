using Xunit;

namespace Ghostty.Tests.Windows.Settings;

public class ProfilesPageSmokeTests
{
    [Fact]
    public void ProfilesPage_RendersVisibleAndHiddenProfilesInOneFlatList()
    {
        // Wire-up plan when WinUITestHost lands:
        //   1. Build a SettingsWindow on the WinUITestHost dispatcher.
        //   2. Inject a FakeProfileRegistry with 2 visible + 1 hidden
        //      profile and a no-op IConfigFileEditor.
        //   3. Navigate to the "profiles" tab.
        //   4. Assert ProfilesGroup.Cards.Count == 3 with toggle states
        //      off/off/on in registry-order.
        //   5. Toggle the first row on; assert the editor saw a
        //      SetValue("profile.<id>.hidden", "true") call.
        //   6. Toggle the third row off; assert the editor saw a
        //      RemoveValue("profile.<id>.hidden") call.
        //
        // Until WinUITestHost exists (also blocking PR 4 tasks 17/18 and
        // PR 5 chord smoke), this stub fails so the test surface is
        // visible in CI counts and reviewers can grep for the string.
        Assert.Fail("TODO: wire WinUITestHost (also blocks PR 4 tasks 17/18 + PR 5 chord smoke)");
    }
}
