using System;
using System.Collections.Generic;
using System.Text.Json;
using Dsht.Toolbox.Core;
using Dsht.Toolbox.Core.Diag;
using Dsht.Toolbox.Core.Hud;
using Dsht.Toolbox.Core.Live;
using System.IO;
using System.IO.Compression;

namespace Dsht.Toolbox.Tests
{
    /// <summary>
    /// Dependency-free assertion runner. Every check is named for the behaviour it protects, and
    /// JSON checks parse the document instead of searching it for substrings.
    /// </summary>
    internal static class Program
    {
        static int passed;
        static int failed;

        static void Check(string name, bool ok)
        {
            if (ok) { passed++; Console.WriteLine("  [PASS] " + name); }
            else { failed++; Console.WriteLine("  [FAIL] " + name); }
        }

        static int Main()
        {
            Console.WriteLine("== dsht tests ==");

            VersionTests();
            PlannerTests();
            RendererTests();
            SurfaceTests();
            SnapshotTests();
            HudContentTests();
            HudLayoutTests();
            PngTests();

            Console.WriteLine();
            Console.WriteLine("== " + passed + " passed, " + failed + " failed ==");
            return failed == 0 ? 0 : 1;
        }

        static void VersionTests()
        {
            Console.WriteLine("-- VersionFacts");
            Check("parses a release installer filename without inventing a tag",
                VersionFacts.Parse("deepseek-harness-0.2.2-win-x64.exe") == "0.2.2");
            Check("parses a pre-release installer filename",
                VersionFacts.Parse("deepseek-harness-0.2.2-rc.5-win-x64.exe") == "0.2.2-rc.5");
            Check("parses a bare version", VersionFacts.Parse("0.2.2") == "0.2.2");
            Check("rejects text without a version", VersionFacts.Parse("no-version-here") == null);
            Check("rejects a bare integer", VersionFacts.Parse("v5") == null);
            Check("rc.5 outranks rc.4", VersionFacts.Compare("0.2.2-rc.5", "0.2.2-rc.4") == 1);
            Check("rc.10 outranks rc.9 numerically", VersionFacts.Compare("0.2.2-rc.10", "0.2.2-rc.9") == 1);
            Check("a release outranks its pre-release", VersionFacts.Compare("0.2.2", "0.2.2-rc.5") == 1);
            Check("a numeric pre-release segment outranks an alphanumeric one", VersionFacts.Compare("1.2.3-1", "1.2.3-0a") == 1);
            Check("identical versions compare equal", VersionFacts.Compare("0.2.1-rc.3", "0.2.1-rc.3") == 0);
            Check("unparsable input yields null, not a guess", VersionFacts.Compare("x", "0.2.2") == null);
            Check("assembly normalisation: 0.2.2.0 equals 0.2.2", VersionFacts.Compare("0.2.2.0", "0.2.2") == 0);
            Check("SameCore ignores the pre-release tag", VersionFacts.SameCore("0.2.2-rc.5", "0.2.2.0"));
            Check("SameCore rejects different cores", !VersionFacts.SameCore("0.2.1", "0.2.2"));
            Check("SameCore rejects unparsable input", !VersionFacts.SameCore("x", "0.2.2"));
            Check("a parsed version parses to itself", VersionFacts.Parse(VersionFacts.Parse("0.2.2-rc.5")) == "0.2.2-rc.5");
        }

        static void PlannerTests()
        {
            Console.WriteLine("-- RemedyPlanner (diagnosis only)");

            Check("an unreadable install directory blocks and asks for elevation",
                HasFinding(RemedyPlanner.Plan(new DiagFacts { InstallDirPresence = Presence.Unreadable }), "install-unreadable", FindingLevel.Block));
            Check("an absent install directory blocks",
                HasFinding(RemedyPlanner.Plan(new DiagFacts { InstallDirPresence = Presence.Absent }), "install-missing", FindingLevel.Block));

            DiagPlan healthy = RemedyPlanner.Plan(Healthy());
            Check("a healthy install is healthy", healthy.Healthy);
            Check("a healthy install needs no attention", !healthy.NeedsAttention);
            Check("a healthy install still reports the main executable", HasFinding(healthy, "main-exe", FindingLevel.Ok));

            DiagFacts running = Healthy();
            running.Holders.Add(new HolderFact { Pid = 11, InsideInstallDir = true });
            DiagPlan runningPlan = RemedyPlanner.Plan(running);
            Check("a running app is informational, not a problem", HasFinding(runningPlan, "app-running", FindingLevel.Info));
            Check("a running app alone keeps the install healthy", runningPlan.Healthy);

            DiagFacts noExe = Healthy();
            noExe.MainExePresence = Presence.Absent;
            Check("a missing main executable blocks", HasFinding(RemedyPlanner.Plan(noExe), "main-exe-missing", FindingLevel.Block));

            DiagFacts noUninstaller = Healthy();
            noUninstaller.UninstallerPresence = Presence.Absent;
            DiagPlan noUninstallerPlan = RemedyPlanner.Plan(noUninstaller);
            Check("a missing uninstaller warns", HasFinding(noUninstallerPlan, "uninstaller-missing", FindingLevel.Warn));
            Check("a missing uninstaller is not a blocker", !HasFinding(noUninstallerPlan, "uninstaller-missing", FindingLevel.Block));
            Check("a warning names a manual remedy", AdviceOf(noUninstallerPlan, "uninstaller-missing").Length > 0);

            DiagFacts gaps = Healthy();
            gaps.PayloadGaps.Add("resources");
            Check("payload gaps are reported", HasFinding(RemedyPlanner.Plan(gaps), "payload-gaps", FindingLevel.Warn));

            DiagFacts unknownProcesses = Healthy();
            unknownProcesses.ProcessEnumerationFailed = true;
            Check("an unenumerable process table is reported as unknown",
                HasFinding(RemedyPlanner.Plan(unknownProcesses), "holders-unknown", FindingLevel.Info));

            DiagFacts staged = Healthy();
            staged.Updater = UpdaterState.Pending;
            staged.PendingExeName = "deepseek-harness-0.2.3-win-x64.exe";
            staged.PendingVersion = "0.2.3";
            staged.PendingSha512 = "abc";
            staged.ProductVersion = "0.2.2";
            DiagPlan stagedPlan = RemedyPlanner.Plan(staged);
            Check("a newer staged installer is reported, not acted on", HasFinding(stagedPlan, "updater-newer", FindingLevel.Info));
            Check("a newer staged installer keeps the install healthy", stagedPlan.Healthy);

            DiagFacts oldStaged = Healthy();
            oldStaged.Updater = UpdaterState.Pending;
            oldStaged.PendingVersion = "0.2.1";
            oldStaged.ProductVersion = "0.2.2";
            Check("an older staged installer is not treated as a fault",
                HasFinding(RemedyPlanner.Plan(oldStaged), "updater-staged", FindingLevel.Ok));

            DiagFacts ambiguous = Healthy();
            ambiguous.Updater = UpdaterState.Ambiguous;
            Check("several staged installers warn", HasFinding(RemedyPlanner.Plan(ambiguous), "updater-ambiguous", FindingLevel.Warn));

            DiagFacts unverified = Healthy();
            unverified.Updater = UpdaterState.PendingUnverified;
            unverified.PendingVersion = "0.2.3";
            unverified.ProductVersion = "0.2.2";
            Check("an unverified staged package is still only a report",
                HasFinding(RemedyPlanner.Plan(unverified), "updater-unreadable", FindingLevel.Info) ||
                HasFinding(RemedyPlanner.Plan(unverified), "updater-newer", FindingLevel.Info));

            Check("null facts produce a blocker instead of a crash",
                HasFinding(RemedyPlanner.Plan(null), "no-facts", FindingLevel.Block));
        }

        static void RendererTests()
        {
            Console.WriteLine("-- ReportRenderer");

            DiagFacts facts = Healthy();
            facts.Holders.Add(new HolderFact { Pid = 11, InsideInstallDir = true });
            DiagPlan plan = RemedyPlanner.Plan(facts);

            using (JsonDocument document = JsonDocument.Parse(ReportRenderer.Json(facts, plan)))
            {
                JsonElement root = document.RootElement;
                Check("json is parseable", root.ValueKind == JsonValueKind.Object);
                Check("json reports health as a boolean", root.GetProperty("healthy").ValueKind == JsonValueKind.True);
                Check("json reports needsAttention as a boolean", root.GetProperty("needsAttention").ValueKind == JsonValueKind.False);
                Check("json carries the install dir", root.GetProperty("installDir").GetString() == facts.InstallDir);
                Check("json findings include the app-running code", HasJsonCode(root, "app-running"));
                Check("json findings carry advice", FindJsonAdvice(root, "app-running").Length > 0);
            }

            using (JsonDocument document = JsonDocument.Parse(ReportRenderer.Json(new DiagFacts(), RemedyPlanner.Plan(new DiagFacts()))))
            {
                JsonElement root = document.RootElement;
                Check("a null version serialises as json null", root.GetProperty("productVersion").ValueKind == JsonValueKind.Null);
                Check("an unreadable install reports the presence enum", root.GetProperty("installDirPresence").GetString() == "Absent");
            }

            var tricky = new DiagFacts { InstallDir = "q\"b" + (char)92 + "c" + (char)10 + "d" };
            using (JsonDocument document = JsonDocument.Parse(ReportRenderer.Json(tricky, RemedyPlanner.Plan(tricky))))
            {
                Check("quotes, backslashes and newlines round-trip",
                    document.RootElement.GetProperty("installDir").GetString() == tricky.InstallDir);
            }

            string human = ReportRenderer.Human(facts, plan);
            Check("text names the app-running finding", human.Contains("app-running"));
            Check("text prints an advice line", human.Contains("-> "));
            Check("text says the app is not a problem", human.Contains("Normal."));
            Check("text never suggests a repair flag", !human.Contains("--fix"));

            string blocked = ReportRenderer.Human(new DiagFacts { InstallDirPresence = Presence.Absent },
                RemedyPlanner.Plan(new DiagFacts { InstallDirPresence = Presence.Absent }));
            Check("a blocker renders as blocked", blocked.Contains("verdict: blocked"));

            string clean = ReportRenderer.Human(Healthy(), RemedyPlanner.Plan(Healthy()));
            Check("a clean install renders as clean", clean.Contains("verdict: no problems detected"));
        }

        static void SurfaceTests()
        {
            Console.WriteLine("-- command surface");
            string[] commands = CommandSurface.Commands;
            Check("the surface stays within the cap", commands.Length <= CommandSurface.MaxCommands);
            Check("the cap is the documented eight", CommandSurface.MaxCommands == 8);
            Check("command names are unique", new HashSet<string>(commands).Count == commands.Length);
            Check("diag is known", CommandSurface.IsKnown("diag"));
            Check("port is known", CommandSurface.IsKnown("port"));
            Check("an invented command is rejected", !CommandSurface.IsKnown("deploy"));
            Check("repair is reserved, not implemented", CommandSurface.IsKnown("repair"));
        }

        static void SnapshotTests()
        {
            Console.WriteLine("-- LiveSnapshot");
            const string good = "{\"v\":2,\"generatedAt\":\"2026-11-25T05:00:00Z\",\"seq\":7,\"sessions\":[{\"id\":\"a1\",\"state\":\"active\",\"ctxUsed\":0.5,\"tokensTotal\":1200,\"questions\":2},{\"id\":\"b2\",\"state\":\"parked\"}]}";
            LiveSnapshot snapshot = LiveSnapshot.Parse(good);
            Check("parses a v2 snapshot", snapshot != null && snapshot.Version == 2);
            Check("keeps the sequence number", snapshot.Seq == 7);
            Check("counts sessions", snapshot.SessionCount == 2);
            Check("counts active sessions", snapshot.ActiveCount == 1);
            Check("sums questions", snapshot.QuestionCount == 2);
            Check("reports peak context", snapshot.PeakContextUsed.HasValue && snapshot.PeakContextUsed.Value == 0.5);
            Check("missing per-session fields stay null", snapshot.Sessions[1].ContextUsed == null);

            Check("rejects a snapshot from another schema version",
                LiveSnapshot.Parse("{\"v\":1,\"sessions\":[]}") == null);
            Check("rejects malformed json", LiveSnapshot.Parse("{not json") == null);
            Check("rejects an empty document", LiveSnapshot.Parse("") == null);
            Check("rejects an array root", LiveSnapshot.Parse("[]") == null);

            LiveSnapshot bare = LiveSnapshot.Parse("{\"v\":2,\"sessions\":[{\"state\":\"active\"}]}");
            Check("drops a session without an id", bare.SessionCount == 0);

            Check("an all-unknown snapshot reports unknown tokens",
                LiveSnapshot.Parse("{\"v\":2,\"sessions\":[{\"id\":\"x\"}]}").TokensTotal == null);
            Check("a snapshot without states reports active as unknown",
                LiveSnapshot.Parse("{\"v\":2,\"sessions\":[{\"id\":\"x\"}]}").ActiveCount == null);
        }

        static void HudContentTests()
        {
            Console.WriteLine("-- HudContent");
            DateTimeOffset now = new DateTimeOffset(2026, 11, 25, 5, 0, 0, TimeSpan.Zero);

            HudContent none = HudContent.From(null, now);
            Check("no snapshot reports unknown everywhere", none.State == HudState.Unknown && none.Stale);
            Check("no snapshot never shows a zero", RowValue(none, "context") == "unknown");
            Check("no snapshot fills all three rows", none.Rows.Count == 3);

            LiveSnapshot fresh = LiveSnapshot.Parse("{\"v\":2,\"generatedAt\":\"2026-11-25T04:59:50Z\",\"sessions\":[{\"id\":\"a\",\"state\":\"active\",\"ctxUsed\":0.38,\"tokensTotal\":12400,\"questions\":1}]}");
            HudContent content = HudContent.From(fresh, now);
            Check("a fresh snapshot is not stale", !content.Stale);
            Check("headline counts sessions", content.Headline.Contains("1 session"));
            Check("the context row is a percentage", RowValue(content, "context").Contains("38%"));
            Check("the token row is abbreviated", RowValue(content, "tokens").Contains("12.4k"));
            Check("the waiting row counts questions", RowValue(content, "waiting").Contains("1 question"));
            Check("rows are label and value pairs", content.Rows.Count == 3 && content.Rows[0].Label == "context");
            Check("a waiting question sets the waiting state", content.State == HudState.Waiting);

            LiveSnapshot parked = LiveSnapshot.Parse("{\"v\":2,\"generatedAt\":\"2026-11-25T04:59:50Z\",\"sessions\":[{\"id\":\"a\",\"state\":\"parked\"}]}");
            Check("a parked session is idle", HudContent.From(parked, now).State == HudState.Idle);

            LiveSnapshot noState = LiveSnapshot.Parse("{\"v\":2,\"generatedAt\":\"2026-11-25T04:59:50Z\",\"sessions\":[{\"id\":\"a\"}]}");
            HudContent unknownState = HudContent.From(noState, now);
            Check("without a state the dot is unknown, not idle", unknownState.State == HudState.Unknown);
            Check("without a state the headline does not claim active", !unknownState.Headline.Contains("active"));

            LiveSnapshot old = LiveSnapshot.Parse("{\"v\":2,\"generatedAt\":\"2026-11-25T04:00:00Z\",\"sessions\":[]}");
            Check("an old snapshot is marked stale", HudContent.From(old, now).Stale);

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

        static void PngTests()
        {
            Console.WriteLine("-- Png");
            var canvas = new Canvas(3, 2);
            canvas.Clear(10, 20, 30, 255);
            canvas.FillRect(1, 1, 1, 1, 200, 100, 50, 255);
            byte[] png = canvas.ToPng();

            Check("starts with the png signature", png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71);
            Check("declares an IHDR chunk", png[12] == (byte)'I' && png[13] == (byte)'H' && png[14] == (byte)'D' && png[15] == (byte)'R');

            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            Check("encodes the requested width", width == 3);
            Check("encodes the requested height", height == 2);

            Check("ends with an IEND marker", png[png.Length - 8] == (byte)'I' && png[png.Length - 7] == (byte)'E');

            byte[] decoded = DecodeIdat(png);
            Check("the payload decodes to (stride+1)*height", decoded.Length == (3 * 4 + 1) * 2);
            Check("every scanline uses filter 0", decoded[0] == 0 && decoded[13] == 0);

            int pixel = 1 + (1 * (3 * 4 + 1)) + 1 * 4;
            Check("pixels round-trip through the deflate stream",
                decoded[pixel] == 200 && decoded[pixel + 1] == 100 && decoded[pixel + 2] == 50);

            // The empty IEND chunk has a well-known CRC; if this fails, every chunk checksum is wrong.
            byte[] iendType = new byte[] { (byte)'I', (byte)'E', (byte)'N', (byte)'D' };
            Check("the crc of IEND matches the published constant",
                Crc32.Compute(iendType, 0, 4) == 0xAE426082u);
            Check("crc state can be chained without double inversion",
                Crc32.Finish(Crc32.Update(Crc32.Update(Crc32.Begin(), iendType, 0, 2), iendType, 2, 2)) == 0xAE426082u);

            Check("every chunk crc in the encoded png verifies",
                AllChunkCrcsValid(png));

            Check("a mismatched buffer is rejected",
                Throws(() => Png.Encode(2, 2, new byte[4])));
            Check("an empty canvas is rejected", Throws(() => Png.Encode(0, 1, new byte[0])));
        }

        static bool AllChunkCrcsValid(byte[] png)
        {
            int position = 8;
            while (position + 8 <= png.Length)
            {
                int length = (png[position] << 24) | (png[position + 1] << 16) | (png[position + 2] << 8) | png[position + 3];

                uint state = Crc32.Update(Crc32.Begin(), png, position + 4, 4);
                state = Crc32.Update(state, png, position + 8, length);
                uint expected = Crc32.Finish(state);

                int crcOffset = position + 8 + length;
                uint stored = ((uint)png[crcOffset] << 24) | ((uint)png[crcOffset + 1] << 16) |
                              ((uint)png[crcOffset + 2] << 8) | png[crcOffset + 3];

                if (stored != expected) return false;
                position = crcOffset + 4;
            }
            return true;
        }

        static byte[] DecodeIdat(byte[] png)
        {
            using (var idat = new MemoryStream())
            {
                int position = 8;
                while (position < png.Length)
                {
                    int length = (png[position] << 24) | (png[position + 1] << 16) | (png[position + 2] << 8) | png[position + 3];
                    string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
                    if (type == "IDAT") idat.Write(png, position + 8, length);
                    position += 8 + length + 4;
                }

                idat.Position = 0;
                using (var zlib = new ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    zlib.CopyTo(output);
                    return output.ToArray();
                }
            }
        }

        static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
        }

        static string RowValue(HudContent content, string label)
        {
            foreach (HudRow row in content.Rows)
                if (row.Label == label) return row.Value;
            return null;
        }

        static DiagFacts Healthy()
        {
            return new DiagFacts
            {
                InstallDir = "C:" + (char)92 + "Apps" + (char)92 + "DeepSeek Harness",
                UpdaterDir = "C:" + (char)92 + "Updater",
                InstallDirPresence = Presence.Present,
                MainExePresence = Presence.Present,
                UninstallerPresence = Presence.Present,
                AppVersion = "0.2.2-rc.5",
                ProductVersion = "0.2.2.0",
                Updater = UpdaterState.Clean,
            };
        }

        static bool HasFinding(DiagPlan plan, string code, FindingLevel level)
        {
            foreach (Finding finding in plan.Findings)
                if (finding.Code == code && finding.Level == level) return true;
            return false;
        }

        static string AdviceOf(DiagPlan plan, string code)
        {
            foreach (Finding finding in plan.Findings)
                if (finding.Code == code) return finding.Advice;
            return string.Empty;
        }

        static bool HasJsonCode(JsonElement root, string code)
        {
            foreach (JsonElement finding in root.GetProperty("findings").EnumerateArray())
                if (finding.GetProperty("code").GetString() == code) return true;
            return false;
        }

        static string FindJsonAdvice(JsonElement root, string code)
        {
            foreach (JsonElement finding in root.GetProperty("findings").EnumerateArray())
                if (finding.GetProperty("code").GetString() == code) return finding.GetProperty("advice").GetString();
            return string.Empty;
        }
    }
}