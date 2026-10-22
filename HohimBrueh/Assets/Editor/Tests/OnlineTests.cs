using FrogSmashers.Netcode;
using FrogSmashers.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FrogSmashers.Tests
{
    /// <summary>The pieces of online play that don't need a network.</summary>
    [TestFixture]
    public class OnlineTests
    {
        [Test]
        public void TheCodecRoundTripsEveryField()
        {
            var codec = new FrogInputCodec();
            var input = FrogInput.Make(right: true, up: true, b: true, x: true, start: true);
            input.XAxis = 127;
            input.YAxis = -64;
            var buffer = new byte[10];

            codec.Write(input, buffer, 3);

            Assert.That(codec.Size, Is.EqualTo(4));
            Assert.That(codec.Read(buffer, 3), Is.EqualTo(input));
            Assert.That(buffer[0] | buffer[1] | buffer[2] | buffer[7], Is.Zero, "writes only its own 4 bytes");
        }

        [Test]
        public void TheCodecUsesAFixedByteOrder()
        {
            var codec = new FrogInputCodec();
            var buffer = new byte[4];

            codec.Write(FrogInput.FromBits(0x04030201u), buffer, 0);

            Assert.That(buffer, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }

        // The fields a test reads back from a Match summary. JsonUtility ignores the rest, and
        // throws if the text isn't JSON.
        private class SummaryFields
        {
            public string code;
            public int slot;
            public string ended;
            public int error_count;
        }

        private static string FinishReport(System.Action duringMatch)
        {
            string sent = null;
            var report = new MatchReport(new OnlineMatchStart { RoomCode = "ABCDE", SendMatchSummary = json => sent = json });
            duringMatch();
            report.Finish("Your friend left.", 1);
            report.Finish("again", 1);
            return sent;
        }

        [Test]
        public void TheMatchSummaryGroupsRepeatedErrors()
        {
            string json = FinishReport(() =>
            {
                for (int i = 0; i < 3; i++)
                {
                    LogAssert.Expect(LogType.Error, "Saved State layout mismatch\nat Tick " + i);
                    Debug.LogError("Saved State layout mismatch\nat Tick " + i);
                }
                LogAssert.Expect(LogType.Error, "Something else");
                Debug.LogError("Something else");
                Debug.LogWarning("Warnings aren't errors");
            });

            var fields = JsonUtility.FromJson<SummaryFields>(json);
            Assert.That(fields.code, Is.EqualTo("ABCDE"));
            Assert.That(fields.slot, Is.EqualTo(1));
            Assert.That(fields.ended, Is.EqualTo("Your friend left."), "sent once; a second Finish does nothing");
            Assert.That(fields.error_count, Is.EqualTo(4));
            Assert.That(json, Does.Contain("{\"message\":\"Saved State layout mismatch\",\"count\":3,\"first_round\":0}"),
                "grouped by the first line of the message");
            Assert.That(json, Does.Contain("{\"message\":\"Something else\",\"count\":1,\"first_round\":0}"));
        }

        [Test]
        public void TheMatchSummaryFitsTheServersLimit()
        {
            string json = FinishReport(() =>
            {
                // More kinds of error than the summary keeps, each longer than it keeps, with
                // characters JSON must escape.
                for (int i = 0; i < 8; i++)
                {
                    string message = i + " \"quoted\" \\ " + new string('x', 300);
                    LogAssert.Expect(LogType.Error, message);
                    Debug.LogError(message);
                }
            });

            Assert.That(JsonUtility.FromJson<SummaryFields>(json).error_count, Is.EqualTo(8));
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.LessThanOrEqualTo(Hootenanny.Protocol.Messages.MaxMatchSummaryBytes));
            Assert.DoesNotThrow(() => Hootenanny.Protocol.Messages.MatchSummary(json));
        }
    }
}
