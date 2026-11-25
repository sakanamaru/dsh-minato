using System;
using System.Collections.Generic;
using System.Text.Json;
using Dsht.Toolbox.Core;
using Dsht.Toolbox.Core.Diag;

namespace Dsht.Toolbox.Tests
{
    /// <summary>Assertions about rendered output and the public command surface.</summary>
    internal static partial class Program
    {
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
    }
}