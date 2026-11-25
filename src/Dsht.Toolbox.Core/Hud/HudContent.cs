using System;
using System.Collections.Generic;
using System.Globalization;
using Dsht.Toolbox.Core.Live;

namespace Dsht.Toolbox.Core.Hud
{
    public enum HudState { Unknown, Idle, Working, Waiting }

    /// <summary>One label/value row. Split rather than pre-joined so the layout can align values.</summary>
    public sealed class HudRow
    {
        public string Label = string.Empty;
        public string Value = string.Empty;
    }

    /// <summary>
    /// What the overlay says, computed from a snapshot and a clock. Pure, so every state and every
    /// formatting rule is asserted without a window.
    /// </summary>
    public sealed class HudContent
    {
        public HudState State;
        public string Headline = string.Empty;
        public List<HudRow> Rows = new List<HudRow>();
        public bool Stale;

        /// <summary>A snapshot older than this is shown as stale rather than as current numbers.</summary>
        public static readonly TimeSpan FreshWindow = TimeSpan.FromSeconds(45);

        public static HudContent From(LiveSnapshot snapshot, DateTimeOffset now)
        {
            var content = new HudContent();

            if (snapshot == null)
            {
                content.State = HudState.Unknown;
                content.Headline = "no snapshot";
                content.Rows.Add(Row("context", "unknown"));
                content.Rows.Add(Row("tokens", "unknown"));
                content.Rows.Add(Row("project", "unknown"));
                content.Stale = true;
                return content;
            }

            content.Stale = snapshot.GeneratedAt.HasValue &&
                            (now - snapshot.GeneratedAt.Value) > FreshWindow;

            content.State = ResolveState(snapshot);
            content.Headline = ComposeHeadline(snapshot);

            // All three rows describe the focus session, so the numbers are comparable. Summing
            // across sessions was the first version and produced a meaningless total: nine
            // sessions, some days old, cache reads included.
            LiveSession focus = snapshot.Focus;

            content.Rows.Add(Row("context", focus != null && focus.ContextUsed.HasValue
                ? Percent(focus.ContextUsed.Value)
                : "unknown"));

            content.Rows.Add(Row("tokens", focus != null && focus.TokensTotal.HasValue
                ? Tokens(focus.TokensTotal.Value)
                : "unknown"));

            content.Rows.Add(Row("project", focus != null && !string.IsNullOrEmpty(focus.ProjectName)
                ? focus.ProjectName
                : "unknown"));

            return content;
        }

        static string ComposeHeadline(LiveSnapshot snapshot)
        {
            if (snapshot.SessionCount == 0) return "no sessions";

            string noun = snapshot.SessionCount == 1 ? " session" : " sessions";
            if (!snapshot.ActiveCount.HasValue) return snapshot.SessionCount + noun;

            int active = snapshot.ActiveCount.Value;
            if (active == 0)
            {
                // Saying "0 active" on an idle machine is noise; the dot already shows it is idle.
                return snapshot.SessionCount + noun;
            }
            return snapshot.SessionCount + noun + " / " + active + " active";
        }

        static HudRow Row(string label, string value)
        {
            return new HudRow { Label = label, Value = value };
        }

        static HudState ResolveState(LiveSnapshot snapshot)
        {
            if (snapshot == null || snapshot.SessionCount == 0) return HudState.Idle;
            if (!snapshot.ActiveCount.HasValue) return HudState.Unknown;
            return snapshot.ActiveCount.Value > 0 ? HudState.Working : HudState.Idle;
        }

        /// <summary>0.38 becomes "38%"; clamped so a bad snapshot cannot draw outside.</summary>
        public static string Percent(double ratio)
        {
            double clamped = ratio < 0 ? 0 : (ratio > 1 ? 1 : ratio);
            return ((int)Math.Round(clamped * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Token counts stay short: 999, 12.4k, 3.1M.</summary>
        public static string Tokens(long count)
        {
            if (count < 0) count = 0;
            if (count < 1000) return count.ToString(CultureInfo.InvariantCulture);
            if (count < 1000000) return (count / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            if (count < 1000000000L) return (count / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            return (count / 1000000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "G";
        }
    }
}