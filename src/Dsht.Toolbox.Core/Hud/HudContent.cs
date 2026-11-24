using System;
using System.Collections.Generic;
using System.Globalization;
using Dsht.Toolbox.Core.Live;

namespace Dsht.Toolbox.Core.Hud
{
    public enum HudState { Unknown, Idle, Working, Waiting }

    /// <summary>One label/value row. Split rather than pre-joined so the layout can align the
    /// values in a column; concatenating them would leave the column ragged.</summary>
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
                content.Rows.Add(Row("waiting", "unknown"));
                content.Stale = true;
                return content;
            }

            content.Stale = snapshot.GeneratedAt.HasValue &&
                            (now - snapshot.GeneratedAt.Value) > FreshWindow;

            content.State = ResolveState(snapshot);
            string noun = snapshot.SessionCount == 1 ? " session" : " sessions";
            content.Headline = snapshot.SessionCount == 0
                ? "no sessions"
                : snapshot.ActiveCount.HasValue
                    ? snapshot.SessionCount + noun + " / " + snapshot.ActiveCount.Value + " active"
                    : snapshot.SessionCount + noun;

            content.Rows.Add(Row("context", snapshot.PeakContextUsed.HasValue
                ? Percent(snapshot.PeakContextUsed.Value)
                : "unknown"));

            content.Rows.Add(Row("tokens", snapshot.TokensTotal.HasValue
                ? Tokens(snapshot.TokensTotal.Value)
                : "unknown"));

            int questions = snapshot.QuestionCount;
            content.Rows.Add(Row("waiting", questions == 0
                ? "none"
                : questions + (questions == 1 ? " question" : " questions")));

            return content;
        }

        static HudRow Row(string label, string value)
        {
            return new HudRow { Label = label, Value = value };
        }

        static HudState ResolveState(LiveSnapshot snapshot)
        {
            if (snapshot == null || snapshot.SessionCount == 0) return HudState.Idle;
            if (snapshot.QuestionCount > 0) return HudState.Waiting;
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
            return (count / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }
    }
}