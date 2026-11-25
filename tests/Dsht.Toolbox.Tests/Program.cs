using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Dsht.Toolbox.Core;
using Dsht.Toolbox.Core.Diag;
using Dsht.Toolbox.Core.Hud;
using Dsht.Toolbox.Core.Live;

namespace Dsht.Toolbox.Tests
{
    /// <summary>
    /// Dependency-free assertion runner. Every check is named for the behaviour it protects, and
    /// JSON checks parse the document instead of searching it for substrings.
    /// </summary>
    internal static partial class Program
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
    }
}
