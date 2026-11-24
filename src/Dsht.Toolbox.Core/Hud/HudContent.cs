using System;
using System.Globalization;
using System.Text;
using Dsht.Toolbox.Core.Live;

namespace Dsht.Toolbox.Core.Hud
{
    public enum HudState { Unknown, Idle, Working, Waiting }

    /// <summary>
    /// The four lines the HUD shows. Computed from a snapshot and a clock, so it can be tested
    /// without a window; formatting lives here so the painter never decides what a number means.
    /// </summary>
    public sealed class HudContent
    {
        public HudState State;
        public string Headline = string.Empty;
        public string ContextLine = string.Empty;
        public string TokensLine = string.Empty;
        public string WaitingLine = string.Empty;
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
                content.ContextLine = "context  unknown";
                content.TokensLine = "this hour  unknown";
                content.WaitingLine = "waiting  unknown";
                content.Stale = true;
                return content;
            }

            content.Stale = snapshot.GeneratedAt.HasValue &&
                            (now - snapshot.GeneratedAt.Value) > FreshWindow;

            content.State = ResolveState(snapshot);
            content.Headline = snapshot.SessionCount == 0
                ? "no sessions"
                : snapshot.SessionCount + " session" + (snapshot.SessionCount == 1 ? string.Empty : "s") +
                  " / " + snapshot.ActiveCount + " active";

            double? context = snapshot.PeakContextUsed;
            content.ContextLine = "context  " + (context.HasValue
                ? Percent(context.Value)
                : "unknown");

            long? tokens = snapshot.TokensThisHour;
            content.TokensLine = "this hour  " + (tokens.HasValue ? Tokens(tokens.Value) : "unknown");

            int questions = snapshot.QuestionCount;
            content.WaitingLine = "waiting  " + (questions == 0 ? "none" : questions + " question" + (questions == 1 ? string.Empty : "s"));

            return content;
        }

        static HudState ResolveState(LiveSnapshot snapshot)
        {
            if (snapshot == null || snapshot.SessionCount == 0) return HudState.Idle;
            if (snapshot.QuestionCount > 0) return HudState.Waiting;
            return snapshot.ActiveCount > 0 ? HudState.Working : HudState.Idle;
        }

        /// <summary>0.38 becomes "38%"; values are clamped so a bad snapshot cannot draw outside.</summary>
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

        /// <summary>Every line, in draw order. The painter iterates this and decides nothing.</summary>
        public string[] Lines()
        {
            return new string[] { Headline, ContextLine, TokensLine, WaitingLine };
        }
    }
}