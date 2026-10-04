using System;
using System.Diagnostics;
using DlbPrecision.DesktopClock.Updater;

namespace DlbPrecision.DesktopClock.Tests
{
    // Release notes arrive as GitHub Markdown and are shown as plain text.
    internal static class PlainTextTests
    {
        public static void Run(TestContext t)
        {
            string notes = "## v1.3.1\nAdds a **Color** menu.\nIt remembers your choice.\n\n"
                + "- [DLB Precision](https://www.dlbprecision.com) gradient\n- `#8B43FF` purple\n";
            t.Check(PlainText.FromMarkdown(notes, 4000, "v1.3.1")
                    == "Adds a Color menu. It remembers your choice.\r\n\r\n• DLB Precision gradient\r\n• #8B43FF purple",
                "Headings repeating the title, bold, code and link markup are removed; bullets become •; paragraph lines are joined");
            t.Check(PlainText.FromMarkdown("# Other heading\ntext", 4000, "v1.3.1") == "Other heading\r\ntext",
                "A heading that isn't the title stays, on its own line");
            t.Check(PlainText.FromMarkdown("1. first\n2) second", 4000, null) == "1. first\r\n2) second", "Numbered items stay one per line");
            t.Check(PlainText.FromMarkdown(null, 4000, null) == "" && PlainText.FromMarkdown("  \n \n", 4000, null) == "", "Empty notes are empty");
            string longText = PlainText.FromMarkdown(new string('a', 5000), 100, null);
            t.Check(longText.Length == 100 && longText.EndsWith("…"), "Long notes are cut with an ellipsis");

            var clock = Stopwatch.StartNew();
            string hostile = PlainText.FromMarkdown(new string('[', 1000000) + "](" + new string('(', 1000000), 4000, null);
            t.Check(clock.ElapsedMilliseconds < 5000 && hostile.Length <= 4000, "Huge or hostile notes are bounded and don't freeze the window");
        }
    }
}
