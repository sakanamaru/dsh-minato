using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Dsht.Toolbox.Core.Live
{
    /// <summary>
    /// Snapshot v2, written by the dsh plugin and read here. Every field that can be unknown is
    /// nullable: a missing value must stay unknown rather than become zero, because a HUD that
    /// shows 0% context when it simply cannot see the session is lying.
    /// </summary>
    public sealed class LiveSnapshot
    {
        public const int CurrentVersion = 2;

        public int Version;
        public DateTimeOffset? GeneratedAt;
        public long Seq;
        public List<LiveSession> Sessions = new List<LiveSession>();

        public int SessionCount { get { return Sessions.Count; } }

        /// <summary>Active sessions, or null when no session reports a state at all.</summary>
        public int? ActiveCount
        {
            get
            {
                int active = 0;
                bool known = false;
                foreach (LiveSession session in Sessions)
                {
                    if (string.IsNullOrEmpty(session.State)) continue;
                    known = true;
                    if (string.Equals(session.State, "active", StringComparison.OrdinalIgnoreCase)) active++;
                }
                return known ? active : (int?)null;
            }
        }

        public int QuestionCount
        {
            get
            {
                int total = 0;
                foreach (LiveSession session in Sessions)
                    if (session.Questions.HasValue) total += session.Questions.Value;
                return total;
            }
        }

        /// <summary>Context pressure of the busiest session, or null when no session reports it.</summary>
        public double? PeakContextUsed
        {
            get
            {
                double? peak = null;
                foreach (LiveSession session in Sessions)
                {
                    if (!session.ContextUsed.HasValue) continue;
                    if (!peak.HasValue || session.ContextUsed.Value > peak.Value) peak = session.ContextUsed;
                }
                return peak;
            }
        }

        /// <summary>Cumulative tokens the meter recorded for every live session, or null when unknown.</summary>
        public long? TokensTotal
        {
            get
            {
                long total = 0;
                bool any = false;
                foreach (LiveSession session in Sessions)
                {
                    if (!session.TokensTotal.HasValue) continue;
                    total += session.TokensTotal.Value;
                    any = true;
                }
                return any ? total : (long?)null;
            }
        }

        /// <summary>Parses a snapshot. Returns null for anything that is not a v2 object.</summary>
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
                    snapshot.Version = Int(root, "v") ?? 0;
                    if (snapshot.Version != CurrentVersion) return null;

                    snapshot.Seq = Int(root, "seq") ?? 0;
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
        public string Project;
        public string State;
        public double? ContextUsed;
        public long? ContextRemainTokens;
        public long? TokensTotal;
        public int? Questions;
        public string LastTool;
        public DateTimeOffset? UpdatedAt;

        public static LiveSession From(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;

            var session = new LiveSession
            {
                Id = LiveSnapshot.Text(element, "id"),
                Project = LiveSnapshot.Text(element, "project"),
                State = LiveSnapshot.Text(element, "state"),
                ContextUsed = LiveSnapshot.Double(element, "ctxUsed"),
                ContextRemainTokens = LiveSnapshot.Long(element, "ctxRemainTokens"),
                TokensTotal = LiveSnapshot.Long(element, "tokensTotal"),
                Questions = LiveSnapshot.Int(element, "questions"),
                LastTool = LiveSnapshot.Text(element, "lastTool"),
                UpdatedAt = LiveSnapshot.Time(element, "updatedAt"),
            };

            return string.IsNullOrEmpty(session.Id) ? null : session;
        }
    }
}