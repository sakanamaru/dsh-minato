using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Dsht.Toolbox.Core.Live
{
    /// <summary>
    /// The status snapshot published by the dsh-minato-bridge plugin (formatVersion 2).
    ///
    /// This mirrors the format that producer actually writes, which was read off a live file rather
    /// than designed here. Every value that a producer may omit stays nullable: a missing number is
    /// unknown, and the overlay says so instead of drawing a zero.
    ///
    /// The snapshot carries session titles. This reader ignores them deliberately - the overlay is a
    /// glanceable status pill, and conversation text does not belong on an always-on-top window.
    /// </summary>
    public sealed class LiveSnapshot
    {
        public const int SupportedFormatVersion = 2;

        public int FormatVersion;
        public DateTimeOffset? GeneratedAt;
        public List<LiveSession> Sessions = new List<LiveSession>();

        public int SessionCount { get { return Sessions.Count; } }

        /// <summary>Sessions the bridge reports as live, or null when it reports none at all.</summary>
        public int? LiveCount
        {
            get
            {
                int live = 0;
                bool known = false;
                foreach (LiveSession session in Sessions)
                {
                    if (!session.Live.HasValue) continue;
                    known = true;
                    if (session.Live.Value) live++;
                }
                return known ? live : (int?)null;
            }
        }

        /// <summary>Sessions the bridge reports as busy right now, or null when it reports none.</summary>
        public int? ActiveCount
        {
            get
            {
                int active = 0;
                bool known = false;
                foreach (LiveSession session in Sessions)
                {
                    if (!session.Active.HasValue) continue;
                    known = true;
                    if (session.Active.Value) active++;
                }
                return known ? active : (int?)null;
            }
        }

        /// <summary>Worst context pressure across sessions, as a fraction of the window.</summary>
        public double? PeakContextUsed
        {
            get
            {
                double? peak = null;
                foreach (LiveSession session in Sessions)
                {
                    double? used = session.ContextUsed;
                    if (!used.HasValue) continue;
                    if (!peak.HasValue || used.Value > peak.Value) peak = used;
                }
                return peak;
            }
        }

        /// <summary>
        /// The session the overlay describes: the busy one if any, otherwise the one prompted most
        /// recently. All three detail rows describe this single session, so the numbers are
        /// comparable; summing across sessions would mix days and cache reads into a meaningless
        /// total.
        /// </summary>
        public LiveSession Focus
        {
            get
            {
                LiveSession focus = null;

                foreach (LiveSession session in Sessions)
                    if (session.Active.HasValue && session.Active.Value) { focus = session; break; }

                if (focus == null)
                {
                    foreach (LiveSession session in Sessions)
                    {
                        if (focus == null) { focus = session; continue; }
                        if (!session.LastPromptAt.HasValue) continue;
                        if (!focus.LastPromptAt.HasValue || session.LastPromptAt.Value > focus.LastPromptAt.Value)
                            focus = session;
                    }
                }

                return focus;
            }
        }

        /// <summary>Parses a snapshot. Null for anything that is not a supported object.</summary>
        public static LiveSnapshot Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;

                    var snapshot = new LiveSnapshot();
                    snapshot.FormatVersion = Int(root, "formatVersion") ?? 0;
                    if (snapshot.FormatVersion != SupportedFormatVersion) return null;

                    snapshot.GeneratedAt = Time(root, "generatedAt");

                    JsonElement sessions;
                    if (root.TryGetProperty("sessions", out sessions) && sessions.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement element in sessions.EnumerateArray())
                        {
                            LiveSession session = LiveSession.From(element);
                            if (session != null) snapshot.Sessions.Add(session);
                        }
                    }

                    return snapshot;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal static int? Int(JsonElement parent, string name)
        {
            JsonElement value;
            if (!parent.TryGetProperty(name, out value)) return null;
            if (value.ValueKind != JsonValueKind.Number) return null;
            int parsed;
            return value.TryGetInt32(out parsed) ? parsed : (int?)null;
        }

        internal static long? Long(JsonElement parent, string name)
        {
            JsonElement value;
            if (!parent.TryGetProperty(name, out value)) return null;
            if (value.ValueKind != JsonValueKind.Number) return null;
            long parsed;
            return value.TryGetInt64(out parsed) ? parsed : (long?)null;
        }

        internal static double? Double(JsonElement parent, string name)
        {
            JsonElement value;
            if (!parent.TryGetProperty(name, out value)) return null;
            if (value.ValueKind != JsonValueKind.Number) return null;
            double parsed;
            return value.TryGetDouble(out parsed) ? parsed : (double?)null;
        }

        internal static bool? Bool(JsonElement parent, string name)
        {
            JsonElement value;
            if (!parent.TryGetProperty(name, out value)) return null;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            return null;
        }

        internal static string Text(JsonElement parent, string name)
        {
            JsonElement value;
            if (!parent.TryGetProperty(name, out value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        internal static DateTimeOffset? Time(JsonElement parent, string name)
        {
            string text = Text(parent, name);
            if (string.IsNullOrEmpty(text)) return null;
            DateTimeOffset parsed;
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed)
                ? parsed
                : (DateTimeOffset?)null;
        }
    }

    public sealed class LiveSession
    {
        public string Id;
        public bool? Live;
        public bool? Active;
        public int? Seq;

        /// <summary>Context window capacity reported by the model adapter, in tokens.</summary>
        public long? ContextWindow;

        /// <summary>Prompt size of the most recent request, per the token meter.</summary>
        public long? PressureTokens;

        public long? UncachedInputTokens;
        public long? OutputTokens;
        public long? CacheReadTokens;
        public long? CacheWriteTokens;

        /// <summary>Project directory of the session; only its last segment is ever displayed.</summary>
        public string Cwd;

        public int? Turns;
        public int? Steps;
        public DateTimeOffset? LastPromptAt;

        /// <summary>Pressure as a fraction of the window; null when either side is unknown.</summary>
        public double? ContextUsed
        {
            get
            {
                if (!PressureTokens.HasValue || !ContextWindow.HasValue) return null;
                if (ContextWindow.Value <= 0) return null;
                double ratio = (double)PressureTokens.Value / ContextWindow.Value;
                return ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
            }
        }

        /// <summary>Last path segment of the working directory, for a glanceable project name.</summary>
        public string ProjectName
        {
            get
            {
                if (string.IsNullOrEmpty(Cwd)) return null;
                string trimmed = Cwd.TrimEnd('\\', '/');
                int slash = trimmed.LastIndexOfAny(new char[] { '\\', '/' });
                string name = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
                return string.IsNullOrEmpty(name) ? null : name;
            }
        }

        /// <summary>Prompt plus output, all four recorded buckets summed; null when none is recorded.</summary>
        public long? TokensTotal
        {
            get
            {
                long?[] parts = { UncachedInputTokens, OutputTokens, CacheReadTokens, CacheWriteTokens };
                long total = 0;
                bool any = false;
                foreach (long? part in parts)
                {
                    if (!part.HasValue) continue;
                    total += part.Value;
                    any = true;
                }
                return any ? total : (long?)null;
            }
        }

        public static LiveSession From(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;

            var session = new LiveSession
            {
                Id = LiveSnapshot.Text(element, "id"),
                Live = LiveSnapshot.Bool(element, "live"),
                Active = LiveSnapshot.Bool(element, "active"),
                Seq = LiveSnapshot.Int(element, "seq"),
                Cwd = LiveSnapshot.Text(element, "cwd"),
                ContextWindow = LiveSnapshot.Long(element, "contextWindow"),
                PressureTokens = LiveSnapshot.Long(element, "pressureTokens"),
                UncachedInputTokens = LiveSnapshot.Long(element, "uncachedInputTokens"),
                OutputTokens = LiveSnapshot.Long(element, "outputTokens"),
                CacheReadTokens = LiveSnapshot.Long(element, "cacheReadTokens"),
                CacheWriteTokens = LiveSnapshot.Long(element, "cacheWriteTokens"),
                Turns = LiveSnapshot.Int(element, "turns"),
                Steps = LiveSnapshot.Int(element, "steps"),
                LastPromptAt = LiveSnapshot.Time(element, "lastPromptAt"),
            };

            return string.IsNullOrEmpty(session.Id) ? null : session;
        }
    }
}