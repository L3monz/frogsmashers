using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FrogSmashers.Netcode;
using Hindsight.Core;
using UnityEngine;

namespace FrogSmashers.Online
{
    /// <summary>
    /// Collects what happened in one Online Match and turns it into one summary: a small JSON
    /// object the lobby sends to the server, which logs it next to its own line for the Room.
    /// Comparing the two players' summaries shows whether their games agreed, without asking
    /// anyone for their log files.
    ///
    /// It records the confirmed Round-over Tick of each Round, every Desync, every error the
    /// game logged (grouped by message, so one repeated exception is one entry with a count),
    /// and the network as the session saw it. It only listens: nothing in the game reads it.
    /// </summary>
    public sealed class MatchReport
    {
        // The server refuses larger summaries (Hootenanny.Protocol.Messages.MaxMatchSummaryBytes).
        private const int MaxBytes = 2048;
        private const int MaxErrorKinds = 5;
        private const int MaxErrorMessageChars = 100;
        private const int MaxDesyncs = 10;

        private sealed class ErrorKind
        {
            public string Message;
            public int Count;
            public int FirstRound;
        }

        private readonly OnlineMatchStart start;
        private readonly float startedAt;
        private readonly long rollbacksAtStart;
        private readonly long rollbackTicksAtStart;

        // Round numbers start at 1; 0 is the setup before the first Round.
        private int round;
        private readonly List<int> roundOverTicks = new List<int>();
        private readonly List<int> desyncRounds = new List<int>();
        private readonly List<int> desyncTicks = new List<int>();
        private int desyncCount;
        private readonly List<ErrorKind> errors = new List<ErrorKind>();
        private int errorCount;
        private readonly List<int> roundTripSamples = new List<int>();
        private readonly List<float> lossSamples = new List<float>();
        private bool sent;

        public MatchReport(OnlineMatchStart start)
        {
            this.start = start;
            startedAt = Time.realtimeSinceStartup;
            rollbacksAtStart = SimulationDriver.TotalRollbacks;
            rollbackTicksAtStart = SimulationDriver.TotalRollbackTicks;
            SimulationDriver.RoundStarted += OnRoundStarted;
            SimulationDriver.OnlineRoundEnded += OnRoundEnded;
            SimulationDriver.OnlineDesyncDetected += OnDesync;
            SimulationDriver.OnlineStatsSampled += OnStats;
            Application.logMessageReceived += OnLogMessage;
        }

        /// <summary>Stops listening and sends the summary, once. <paramref name="ended"/> says how the Match ended.</summary>
        public void Finish(string ended, int localSlot)
        {
            if (sent)
                return;
            sent = true;
            SimulationDriver.RoundStarted -= OnRoundStarted;
            SimulationDriver.OnlineRoundEnded -= OnRoundEnded;
            SimulationDriver.OnlineDesyncDetected -= OnDesync;
            SimulationDriver.OnlineStatsSampled -= OnStats;
            Application.logMessageReceived -= OnLogMessage;

            string json = ToJson(ended, localSlot);
            Debug.Log("[ONLINE] Match summary: " + json);
            if (start.SendMatchSummary != null)
                start.SendMatchSummary(json);
        }

        private void OnRoundStarted()
        {
            if (SimulationDriver.CurrentMode != SimulationDriver.Mode.Online)
                return;
            round++;
            // -1 until the Round ends, so a Round cut short by the Match ending shows as one.
            roundOverTicks.Add(-1);
        }

        private void OnRoundEnded(int tick)
        {
            if (roundOverTicks.Count > 0)
                roundOverTicks[roundOverTicks.Count - 1] = tick;
        }

        private void OnDesync(int tick)
        {
            desyncCount++;
            if (desyncTicks.Count >= MaxDesyncs)
                return;
            desyncRounds.Add(round);
            desyncTicks.Add(tick);
        }

        private void OnStats(NetworkStats stats)
        {
            roundTripSamples.Add(stats.RoundTripMs);
            lossSamples.Add(stats.LossPercent);
        }

        private void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            errorCount++;

            // Group by the first line: a message usually only varies after it.
            string key = message ?? "";
            int newline = key.IndexOf('\n');
            if (newline >= 0)
                key = key.Substring(0, newline);
            if (key.Length > MaxErrorMessageChars)
                key = key.Substring(0, MaxErrorMessageChars);

            foreach (var kind in errors)
            {
                if (kind.Message == key)
                {
                    kind.Count++;
                    return;
                }
            }
            if (errors.Count < MaxErrorKinds)
                errors.Add(new ErrorKind { Message = key, Count = 1, FirstRound = round });
        }

        // Over the server's limit, the summary drops detail until it fits: first the error
        // messages get shorter, then the lists go and only the counts stay.
        private string ToJson(string ended, int localSlot)
        {
            for (int detail = 2; detail > 0; detail--)
            {
                string json = Write(ended, localSlot, detail);
                if (Encoding.UTF8.GetByteCount(json) <= MaxBytes)
                    return json;
            }
            return Write(ended, localSlot, 0);
        }

        private string Write(string ended, int localSlot, int detail)
        {
            var json = new JsonWriter();
            json.String("code", start.RoomCode);
            json.Number("slot", localSlot);
            json.String("build", BuildInfo.Id);
            json.String("ended", ended);
            json.String("path", start.Relayed ? "relay" : "direct");
            json.Number("duration_s", Mathf.RoundToInt(Time.realtimeSinceStartup - startedAt));

            json.Number("rounds", round);
            if (detail > 0)
                json.Numbers("round_over_ticks", roundOverTicks);

            json.Number("desync_count", desyncCount);
            if (detail > 0)
            {
                json.BeginArray("desyncs");
                for (int i = 0; i < desyncTicks.Count; i++)
                {
                    json.BeginObject();
                    json.Number("round", desyncRounds[i]);
                    json.Number("tick", desyncTicks[i]);
                    json.EndObject();
                }
                json.EndArray();
            }

            json.Number("error_count", errorCount);
            if (detail > 0)
            {
                json.BeginArray("errors");
                foreach (var kind in errors)
                {
                    json.BeginObject();
                    json.String("message", detail > 1 ? kind.Message : Shorten(kind.Message, 40));
                    json.Number("count", kind.Count);
                    json.Number("first_round", kind.FirstRound);
                    json.EndObject();
                }
                json.EndArray();
            }

            json.BeginObject("rtt_ms");
            json.Number("median", Median(roundTripSamples));
            json.Number("worst", Max(roundTripSamples));
            json.EndObject();
            json.BeginObject("loss_pct");
            json.Decimal("median", Median(lossSamples));
            json.Decimal("worst", Max(lossSamples));
            json.EndObject();
            json.Number("rollbacks", SimulationDriver.TotalRollbacks - rollbacksAtStart);
            json.Number("ticks_rerun", SimulationDriver.TotalRollbackTicks - rollbackTicksAtStart);
            return json.ToString();
        }

        private static string Shorten(string text, int length) => text.Length > length ? text.Substring(0, length) : text;

        private static int Median(List<int> samples)
        {
            if (samples.Count == 0)
                return 0;
            var sorted = new List<int>(samples);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }

        private static float Median(List<float> samples)
        {
            if (samples.Count == 0)
                return 0;
            var sorted = new List<float>(samples);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }

        private static int Max(List<int> samples)
        {
            int max = 0;
            foreach (int sample in samples)
                max = Mathf.Max(max, sample);
            return max;
        }

        private static float Max(List<float> samples)
        {
            float max = 0;
            foreach (float sample in samples)
                max = Mathf.Max(max, sample);
            return max;
        }

        /// <summary>
        /// Just enough JSON writing for the summary. Unity's JsonUtility can't leave fields out
        /// and prints floats with all their digits, and the summary must stay small.
        /// </summary>
        private sealed class JsonWriter
        {
            private readonly StringBuilder text = new StringBuilder("{");
            private bool needComma;

            public void String(string name, string value)
            {
                Name(name);
                if (value == null)
                {
                    text.Append("null");
                    return;
                }
                text.Append('"');
                foreach (char c in value)
                {
                    switch (c)
                    {
                        case '"': text.Append("\\\""); break;
                        case '\\': text.Append("\\\\"); break;
                        case '\n': text.Append("\\n"); break;
                        case '\r': text.Append("\\r"); break;
                        case '\t': text.Append("\\t"); break;
                        default:
                            if (c < ' ')
                                text.Append("\\u").Append(((int)c).ToString("x4"));
                            else
                                text.Append(c);
                            break;
                    }
                }
                text.Append('"');
            }

            public void Number(string name, long value)
            {
                Name(name);
                text.Append(value.ToString(CultureInfo.InvariantCulture));
            }

            /// <summary>One decimal place: enough for a percentage.</summary>
            public void Decimal(string name, float value)
            {
                Name(name);
                text.Append(value.ToString("0.0", CultureInfo.InvariantCulture));
            }

            public void Numbers(string name, List<int> values)
            {
                BeginArray(name);
                foreach (int value in values)
                {
                    Comma();
                    text.Append(value.ToString(CultureInfo.InvariantCulture));
                }
                EndArray();
            }

            public void BeginObject(string name = null)
            {
                if (name != null)
                    Name(name);
                else
                    Comma();
                text.Append('{');
                needComma = false;
            }

            public void EndObject()
            {
                text.Append('}');
                needComma = true;
            }

            public void BeginArray(string name)
            {
                Name(name);
                text.Append('[');
                needComma = false;
            }

            public void EndArray()
            {
                text.Append(']');
                needComma = true;
            }

            public override string ToString() => text.ToString() + "}";

            private void Name(string name)
            {
                Comma();
                text.Append('"').Append(name).Append("\":");
            }

            private void Comma()
            {
                if (needComma)
                    text.Append(',');
                needComma = true;
            }
        }
    }
}
