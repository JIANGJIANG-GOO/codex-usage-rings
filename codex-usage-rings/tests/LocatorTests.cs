using System;
using System.Collections.Generic;
using System.Drawing;
using CodexUsageRings;

internal static class LocatorTests
{
    private static int failures;
    private static WindowLocator.Candidate Candidate(string name, string id, Rectangle bounds)
    {
        return new WindowLocator.Candidate { Name = name, AutomationId = id, Bounds = bounds, IsButton = true };
    }

    private static void Check(string name, bool result)
    {
        Console.WriteLine((result ? "PASS " : "FAIL ") + name);
        if (!result) failures++;
    }

    public static int Main()
    {
        Rectangle host = new Rectangle(100, 100, 1200, 800);
        Rectangle button = new Rectangle(110, 850, 32, 32);
        string reason;
        Rectangle result = WindowLocator.SelectCandidate(new[] { Candidate("Account", "", button) }, host, 1f, out reason);
        Check("semantic lower-left account control is accepted and normalized to 24 DIP", result == new Rectangle(114, 854, 24, 24));
        result = WindowLocator.SelectCandidate(new[] { Candidate("CJ", "", button) }, host, 1f, out reason);
        Check("initials alone do not identify an account control", result.IsEmpty);
        result = WindowLocator.SelectCandidate(new[] { Candidate("", "sidebar-user-menu-button", button) }, host, 1f, out reason);
        Check("semantic automation ID permits an unnamed control", !result.IsEmpty);
        result = WindowLocator.SelectCandidate(new[] { Candidate("Settings", "", button) }, host, 1f, out reason);
        Check("settings control is rejected", result.IsEmpty);
        result = WindowLocator.SelectCandidate(new[] { Candidate("Account", "", new Rectangle(310, 850, 32, 32)) }, host, 1f, out reason);
        Check("account in conversation content is rejected", result.IsEmpty);
        result = WindowLocator.SelectCandidate(new[] { Candidate("Account", "", new Rectangle(110, 200, 32, 32)) }, host, 1f, out reason);
        Check("account outside bottom rail is rejected", result.IsEmpty);
        result = WindowLocator.SelectCandidate(new[] { Candidate("Account", "", new Rectangle(110, 850, 180, 32)) }, host, 1f, out reason);
        Check("wide account menu row is rejected", result.IsEmpty);
        result = WindowLocator.SelectCandidate(new[] { Candidate("Account", "", button), Candidate("Profile", "", button) }, host, 1f, out reason);
        Check("duplicate accessibility representations have one center", !result.IsEmpty);
        result = WindowLocator.SelectCandidate(new[] { Candidate("Account", "", button), Candidate("Profile", "", new Rectangle(110, 805, 32, 32)) }, host, 1f, out reason);
        Check("two distinct account candidates are rejected as ambiguous", result.IsEmpty && reason.IndexOf("ambiguous", StringComparison.OrdinalIgnoreCase) >= 0);
        result = WindowLocator.SelectCandidate(new[] { Candidate("Add account", "account-button", button) }, host, 1f, out reason);
        Check("account creation controls are rejected", result.IsEmpty);
        WindowLocator.Candidate label = Candidate("Account", "", button);
        label.IsButton = false;
        result = WindowLocator.SelectCandidate(new[] { label }, host, 1f, out reason);
        Check("passive labels are rejected", result.IsEmpty);
        result = WindowLocator.SelectCandidate(new[] { Candidate("账户", "", new Rectangle(-885, 1275, 48, 48)) }, new Rectangle(-900, 150, 1800, 1200), 1.5f, out reason);
        Check("150 percent DPI and negative monitor coordinates normalize correctly", result == new Rectangle(-879, 1281, 36, 36));
        Console.WriteLine("Locator policy: " + (12 - failures) + "/12 passed. No UI Automation or window inspection was invoked.");
        return failures == 0 ? 0 : 1;
    }
}
