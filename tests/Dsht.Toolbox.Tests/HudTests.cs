using System;
using System.Collections.Generic;
using Dsht.Toolbox.Core.Hud;
using Dsht.Toolbox.Core.Live;

namespace Dsht.Toolbox.Tests
{
    /// <summary>Assertions about the snapshot reader and the overlay content.</summary>
    internal static partial class Program
    {
        static void SnapshotTests()
        {
            Console.WriteLine("-- LiveSnapshot (real producer format)");
            const string good = "{\"formatVersion\":2,\"generatedAt\":\"2026-11-25T11:25:44.964Z\",\"sessions\":[" +
                "{\"id\":\"s1\",\"live\":true,\"active\":true,\"seq\":2908,\"turns\":7,\"steps\":263," +
                "\"uncachedInputTokens\":1000,\"outputTokens\":200,\"cacheReadTokens\":50,\"cacheWriteTokens\":10," +
                "\"contextWindow\":1000,\"pressureTokens\":640}," +
                "{\"id\":\"s2\",\"live\":true,\"active\":false}]}";

            LiveSnapshot snapshot = LiveSnapshot.Parse(good);
            Check("parses the producer format", snapshot != null && snapshot.FormatVersion == 2);
            Check("keeps the generation time", snapshot.GeneratedAt.HasValue);
            Check("counts sessions", snapshot.SessionCount == 2);
            Check("counts live sessions", snapshot.LiveCount == 2);
            Check("counts active sessions", snapshot.ActiveCount == 1);
            Check("peak pressure is the worst session", snapshot.PeakContextUsed.HasValue &&
                Math.Abs(snapshot.PeakContextUsed.Value - 0.64) < 0.0001);
            Check("the focus session is the active one", snapshot.Focus != null && snapshot.Focus.Id == "s1");
            Check("per-session pressure is pressure over window", snapshot.Sessions[0].ContextUsed.Value > 0.63 &&
                snapshot.Sessions[0].ContextUsed.Value < 0.65);
            Check("tokens total sums the four buckets", snapshot.Sessions[0].TokensTotal == 1260);
            Check("a session without usage reports unknown tokens", snapshot.Sessions[1].TokensTotal == null);
            Check("a session without a window reports unknown pressure", snapshot.Sessions[1].ContextUsed == null);

            Check("rejects another format version",
                LiveSnapshot.Parse("{\"formatVersion\":1,\"sessions\":[]}") == null);
            Check("rejects a snapshot with no format version",
                LiveSnapshot.Parse("{\"sessions\":[]}") == null);
            Check("rejects malformed json", LiveSnapshot.Parse("{not json") == null);
            Check("rejects an empty document", LiveSnapshot.Parse("") == null);
            Check("rejects an array root", LiveSnapshot.Parse("[]") == null);

            Check("drops a session without an id", LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"sessions\":[{\"active\":true}]}").SessionCount == 0);
            Check("a snapshot without telemetry reports unknown tokens", LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"sessions\":[{\"id\":\"x\"}]}").Focus.TokensTotal == null);

            // Pressure can exceed the window after a model switch; it must clamp, not overflow the bar.
            LiveSnapshot overfull = LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"sessions\":[{\"id\":\"x\",\"contextWindow\":100,\"pressureTokens\":250}]}");
            Check("pressure above the window clamps to one", overfull.PeakContextUsed == 1.0);

            LiveSnapshot zeroWindow = LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"sessions\":[{\"id\":\"x\",\"contextWindow\":0,\"pressureTokens\":5}]}");
            Check("a zero window does not divide by zero", zeroWindow.PeakContextUsed == null);
        }

        static void HudContentTests()
        {
            Console.WriteLine("-- HudContent");
            DateTimeOffset now = new DateTimeOffset(2026, 11, 25, 11, 25, 45, TimeSpan.Zero);

            HudContent none = HudContent.From(null, now);
            Check("no snapshot reports unknown everywhere", none.State == HudState.Unknown && none.Stale);
            Check("no snapshot never shows a zero", RowValue(none, "context") == "unknown");
            Check("no snapshot fills three rows", none.Rows.Count == 3);

            LiveSnapshot busy = LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"generatedAt\":\"2026-11-25T11:25:44Z\",\"sessions\":[" +
                "{\"id\":\"a\",\"live\":true,\"active\":true,\"contextWindow\":1000,\"pressureTokens\":380," +
                "\"uncachedInputTokens\":12000,\"outputTokens\":400}]}");
            HudContent content = HudContent.From(busy, now);
            Check("a fresh snapshot is not stale", !content.Stale);
            Check("headline counts sessions and active ones", content.Headline.Contains("1 session") && content.Headline.Contains("1 active"));
            Check("the context row is a percentage", RowValue(content, "context") == "38%");
            Check("the token row is abbreviated", RowValue(content, "tokens") == "12.4k");
            Check("the project row falls back to unknown without a cwd", RowValue(content, "project") == "unknown");
            Check("no row invents a value the producer cannot supply",
                RowValue(content, "context") != "0%" && RowValue(content, "tokens") != "0");
            Check("an active session sets the working state", content.State == HudState.Working);

            LiveSnapshot withCwd = LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"generatedAt\":\"2026-11-25T11:25:44Z\",\"sessions\":[" +
                "{\"id\":\"a\",\"active\":true,\"cwd\":\"D:\\\\work\\\\dsh-minato\"}," +
                "{\"id\":\"b\",\"active\":false,\"cwd\":\"D:\\\\work\\\\other\"}]}");
            Check("the project row shows the working directory name",
                RowValue(HudContent.From(withCwd, now), "project") == "dsh-minato");

            LiveSnapshot stale = LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"generatedAt\":\"2026-11-25T11:25:44Z\",\"sessions\":[" +
                "{\"id\":\"a\",\"active\":false,\"lastPromptAt\":\"2026-11-20T09:22:39Z\"}," +
                "{\"id\":\"b\",\"active\":false,\"lastPromptAt\":\"2026-11-25T11:00:00Z\"}]}");
            Check("the focus session is the most recently prompted one", stale.Focus.Id == "b");

            LiveSnapshot idle = LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"generatedAt\":\"2026-11-25T11:25:44Z\",\"sessions\":[{\"id\":\"a\",\"live\":true,\"active\":false}]}");
            HudContent idleContent = HudContent.From(idle, now);
            Check("no active session is idle", idleContent.State == HudState.Idle);
            Check("an idle machine does not print a zero active count", !idleContent.Headline.Contains("active"));

            LiveSnapshot noFlags = LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"generatedAt\":\"2026-11-25T11:25:44Z\",\"sessions\":[{\"id\":\"a\"}]}");
            Check("without an active flag the dot is unknown, not idle",
                HudContent.From(noFlags, now).State == HudState.Unknown);

            LiveSnapshot old = LiveSnapshot.Parse(
                "{\"formatVersion\":2,\"generatedAt\":\"2026-11-25T10:00:00Z\",\"sessions\":[]}");
            Check("an old snapshot is marked stale", HudContent.From(old, now).Stale);
            Check("an empty snapshot reports no sessions", HudContent.From(old, now).Headline == "no sessions");

            Check("percent clamps above one", HudContent.Percent(1.7) == "100%");
            Check("percent clamps below zero", HudContent.Percent(-2) == "0%");
            Check("tokens under a thousand stay exact", HudContent.Tokens(999) == "999");
            Check("tokens in thousands are abbreviated", HudContent.Tokens(12400) == "12.4k");
            Check("tokens in millions are abbreviated", HudContent.Tokens(3100000) == "3.1M");
            Check("three rows plus a headline are produced", content.Rows.Count == 3 && !string.IsNullOrEmpty(content.Headline));
        }

        static void HudLayoutTests()
        {
            Console.WriteLine("-- HudLayout");
            HudContent content = HudContent.From(null, DateTimeOffset.UtcNow);

            HudLayout.Measure(100, out int w100, out int h100);
            HudLayout.Measure(150, out int w150, out int h150);
            Check("the canvas is the size the design contract states", w100 == 176 && h100 == 83);
            Check("the canvas scales with dpi", w150 == (int)Math.Round(w100 * 1.5) && h150 > h100);

            var lines = HudLayout.Compute(content, 100);
            Check("headline plus three label/value pairs are laid out", lines.Count == 7);
            Check("only the headline is emphasised", lines[0].Emphasis && !lines[1].Emphasis);
            Check("labels share one left edge", lines[1].X == lines[3].X && lines[3].X == lines[5].X);
            Check("values align in a column", lines[2].X == lines[4].X && lines[4].X == lines[6].X);
            Check("the value column is the declared column", lines[2].X == HudLayout.ValueColumn);
            Check("row pitch sits on the 4-unit grid", HudLayout.RowGap % HudLayout.Grid == 0);
            Check("padding sits on the 4-unit grid", HudLayout.Padding % HudLayout.Grid == 0);
            Check("the value column starts right of the labels", lines[2].X > lines[1].X);
            Check("labels are muted and values are not", lines[1].Muted && !lines[2].Muted);
            Check("rows are ordered top to bottom", lines[1].Y < lines[3].Y && lines[3].Y < lines[5].Y);
            Check("text starts inside the canvas", lines[0].X > 0 && lines[0].X < w100);
            Check("the last row leaves padding below it", lines[6].Y + HudLayout.BodySize < h100);
            Check("the canvas is not taller than its content needs", h100 <= HudLayout.Padding * 2 + HudLayout.RowGap * 3 + HudLayout.BodySize + 1);

            HudLayout.DotRect(100, out int dx, out int dy, out int diameter);
            Check("the dot sits inside the canvas", dx >= 0 && dy >= 0 && dx + diameter <= w100);

            var scaled = HudLayout.Compute(content, 200);
            Check("a higher scale produces larger fonts", scaled[0].Size > lines[0].Size);
        }
        static string RowValue(HudContent content, string label)
        {
            foreach (HudRow row in content.Rows)
                if (row.Label == label) return row.Value;
            return null;
        }
    }
}