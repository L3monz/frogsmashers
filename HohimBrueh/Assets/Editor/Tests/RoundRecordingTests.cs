using System.IO;
using FrogSmashers.Netcode;
using NUnit.Framework;
using UnityEngine;

namespace FrogSmashers.Tests
{
    /// <summary>The replay check's recording: input packing and the file format.</summary>
    [TestFixture]
    public class RoundRecordingTests
    {
        [Test]
        public void InputBitsRoundTripEveryField()
        {
            var input = FrogInput.Make(left: true, down: true, a: true, y: true, start: true);
            input.XAxis = -127;
            input.YAxis = 64;

            Assert.That(FrogInput.FromBits(input.ToBits()), Is.EqualTo(input));
        }

        [Test]
        public void ARollbackRunOverwritesTheTicksItRunsAgain()
        {
            // Online: Ticks 0-3 first run on predictions, then a Rollback runs 2 and 3 again
            // with the real inputs. The recording must keep the second runs.
            var recording = new RoundRecording();
            var inputs = new FrogInput[6];
            for (int tick = 0; tick < 4; tick++)
                recording.SetTick(tick, inputs, checksum: 100u + (uint)tick);

            inputs[1] = FrogInput.Make(left: true);
            recording.SetTick(2, inputs, 202u);
            Assert.That(recording.TickCount, Is.EqualTo(3), "the Ticks after the re-run one are dropped until they run again");
            recording.SetTick(3, inputs, 203u);

            var read = new FrogInput[6];
            recording.GetInputs(2, read);
            Assert.That(read[1].Left, Is.True);
            Assert.That(recording.ChecksumAt(1), Is.EqualTo(101u));
            Assert.That(recording.ChecksumAt(3), Is.EqualTo(203u));

            recording.Truncate(2);
            Assert.That(recording.TickCount, Is.EqualTo(2), "only confirmed Ticks are kept");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => recording.SetTick(5, inputs, 0u));
        }

        [Test]
        public void ARecordingSurvivesWritingAndReading()
        {
            var recording = new RoundRecording
            {
                Level = "3Moon",
                Seed = 0xDEADBEEFCAFEUL,
                TeamMode = true,
                ShowDown = true,
                OnlyBounceBeforeRecover = true,
                Roster = new[] { 4, 0, 2 },
                RecordedCap = -1,
                StartChecksum = 12345u,
            };
            recording.Teams[2] = Team.Red;
            recording.RoundWins[4] = 3;
            recording.Colors[0] = new Color(0.1f, 0.2f, 0.3f, 1f);

            var inputs = new FrogInput[6];
            for (int tick = 0; tick < 100; tick++)
            {
                inputs[tick % 6] = FrogInput.Make(right: tick % 2 == 0, x: tick % 3 == 0);
                recording.AddTick(inputs, (uint)(tick * 7919));
            }
            recording.EndedWithRoundOver = true;

            var stream = new MemoryStream();
            recording.Write(stream);
            var read = RoundRecording.Read(new MemoryStream(stream.ToArray()));

            Assert.That(read.Level, Is.EqualTo("3Moon"));
            Assert.That(read.Seed, Is.EqualTo(0xDEADBEEFCAFEUL));
            Assert.That(read.TeamMode && read.ShowDown && read.OnlyBounceBeforeRecover, Is.True);
            Assert.That(read.PlayersCanDropIn || read.CharactersBounceEachOther || read.WeirdBounceTrajectories, Is.False);
            Assert.That(read.Roster, Is.EqualTo(new[] { 4, 0, 2 }), "the roster order is part of the setup");
            Assert.That(read.Teams[2], Is.EqualTo(Team.Red));
            Assert.That(read.RoundWins[4], Is.EqualTo(3));
            Assert.That(read.Colors[0], Is.EqualTo(new Color(0.1f, 0.2f, 0.3f, 1f)));
            Assert.That(read.RecordedCap, Is.EqualTo(-1));
            Assert.That(read.StartChecksum, Is.EqualTo(12345u));
            Assert.That(read.EndedWithRoundOver, Is.True);
            Assert.That(read.TickCount, Is.EqualTo(100));

            var expected = new FrogInput[6];
            var actual = new FrogInput[6];
            for (int tick = 0; tick < 100; tick++)
            {
                recording.GetInputs(tick, expected);
                read.GetInputs(tick, actual);
                Assert.That(actual, Is.EqualTo(expected), "inputs of tick " + tick);
                Assert.That(read.ChecksumAt(tick), Is.EqualTo((uint)(tick * 7919)));
            }
        }

        [Test]
        public void RecordingGrowsPastItsInitialCapacity()
        {
            var recording = new RoundRecording();
            var inputs = new FrogInput[6];
            int ticks = 60 * 60 * 5 + 10;
            for (int tick = 0; tick < ticks; tick++)
                recording.AddTick(inputs, (uint)tick);

            Assert.That(recording.TickCount, Is.EqualTo(ticks));
            Assert.That(recording.ChecksumAt(ticks - 1), Is.EqualTo((uint)(ticks - 1)));
        }

        [Test]
        public void ClearStartsANewRecording()
        {
            var recording = new RoundRecording();
            recording.AddTick(new FrogInput[6], 1u);
            recording.EndedWithRoundOver = true;

            recording.Clear();

            Assert.That(recording.TickCount, Is.Zero);
            Assert.That(recording.EndedWithRoundOver, Is.False);
        }
    }
}
