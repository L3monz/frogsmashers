using System;
using System.Collections.Generic;
using System.Linq;
using FrogSmashers.Netcode;
using NUnit.Framework;
using UnityEngine;

namespace FrogSmashers.Tests
{
    /// <summary>
    /// Round-trip tests for Simulation State (P2, spec D2-D4).
    ///
    /// For each simulated class: fill an instance with generated values, write it, read the
    /// bytes into a second instance, write that one, and compare. Equal bytes mean SyncState
    /// saves and loads the same fields in the same order, and loses nothing on the way.
    /// </summary>
    [TestFixture]
    public class StateSyncTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private readonly StateStream stream = new StateStream();

        [SetUp]
        public void CreatePlayers()
        {
            // The Player constructor registers each Player by Slot, which references resolve
            // through.
            for (int slot = 0; slot < 6; slot++)
                new Player(FreeLives.InputReader.Device.Keyboard1, Color.white, slot);
        }

        [TearDown]
        public void DestroyObjects()
        {
            foreach (var go in created)
                UnityEngine.Object.DestroyImmediate(go);
            created.Clear();
        }

        private T Make<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            created.Add(go);
            return go.AddComponent<T>();
        }

        private byte[] Write(Action<StateStream> sync)
        {
            stream.BeginWrite();
            sync(stream);
            return stream.EndWrite();
        }

        private void Read(byte[] state, Action<StateStream> sync)
        {
            stream.BeginRead(state);
            sync(stream);
            stream.EndRead();
        }

        private void AssertRoundTrip(Action<StateStream> first, Action<StateStream> second, Action<StateStream> fresh)
        {
            stream.BeginFill(seed: 17);
            first(stream);
            byte[] written = Write(first);

            Read(written, second);
            byte[] rewritten = Write(second);

            Assert.That(rewritten, Is.EqualTo(written), "write -> read -> write must give the same bytes");

            // Every field must hold its filled value, not its default. Fill assigns through the
            // same "if (s.IsReading)" lines as Read, so a field still at its default here is a
            // field SyncState never assigns back: a Rollback would not restore it.
            var filled = Describe(written, first);
            var defaults = Describe(Write(fresh), fresh);
            Assert.That(filled.Select(p => p.Key), Is.EqualTo(defaults.Select(p => p.Key)));
            var notRestored = filled.Where((p, i) => p.Value == defaults[i].Value).Select(p => p.Key).ToList();
            Assert.That(notRestored, Is.Empty, "fields that keep their default after a load");
        }

        private List<KeyValuePair<string, string>> Describe(byte[] state, Action<StateStream> sync)
        {
            stream.BeginDescribe(state);
            sync(stream);
            return stream.EndDescribe();
        }

        // Unity does not call Awake on components added in EditMode tests. Fly.Awake registers
        // the fly that a Character's reference resolves to.
        private Fly MakeActiveFly()
        {
            var fly = Make<Fly>();
            typeof(Fly).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(fly, null);
            return fly;
        }

        // ---------------------------------------------------------------- StateStream itself

        [Test]
        public void PrimitivesKeepTheirExactValues()
        {
            bool b = true;
            int i = int.MinValue;
            int slot = -1;
            float f1 = -0f, f2 = float.NaN, f3 = float.MaxValue, f4 = 1f / 3f;
            ulong u = ulong.MaxValue - 12345;
            var v2 = new Vector2(1.5f, -2.25f);
            var v3 = new Vector3(float.Epsilon, -1e30f, 7f);

            byte[] bytes = Write(s =>
            {
                s.Bool("b", ref b); s.Int("i", ref i); s.Slot("slot", ref slot);
                s.Float("f1", ref f1); s.Float("f2", ref f2); s.Float("f3", ref f3); s.Float("f4", ref f4);
                s.ULong("u", ref u); s.Vector2("v2", ref v2); s.Vector3("v3", ref v3);
            });

            bool rb = false; int ri = 0, rslot = 0; float r1 = 1, r2 = 1, r3 = 1, r4 = 1; ulong ru = 0;
            Vector2 rv2 = default(Vector2); Vector3 rv3 = default(Vector3);
            Read(bytes, s =>
            {
                s.Bool("b", ref rb); s.Int("i", ref ri); s.Slot("slot", ref rslot);
                s.Float("f1", ref r1); s.Float("f2", ref r2); s.Float("f3", ref r3); s.Float("f4", ref r4);
                s.ULong("u", ref ru); s.Vector2("v2", ref rv2); s.Vector3("v3", ref rv3);
            });

            Assert.That(rb, Is.EqualTo(b));
            Assert.That(ri, Is.EqualTo(i));
            Assert.That(rslot, Is.EqualTo(-1));
            Assert.That(BitConverter.DoubleToInt64Bits(r1), Is.EqualTo(BitConverter.DoubleToInt64Bits(f1)), "-0 keeps its sign");
            Assert.That(float.IsNaN(r2), Is.True);
            Assert.That(r3, Is.EqualTo(f3));
            Assert.That(r4, Is.EqualTo(f4));
            Assert.That(ru, Is.EqualTo(u));
            Assert.That(rv2, Is.EqualTo(v2));
            Assert.That(rv3, Is.EqualTo(v3));
        }

        [Test]
        public void ALayoutMismatchIsAnError()
        {
            int a = 1, b = 2;
            byte[] twoInts = Write(s => { s.Int("a", ref a); s.Int("b", ref b); });

            Assert.Throws<InvalidOperationException>(() => Read(twoInts, s => s.Int("a", ref a)),
                "reading fewer fields than were written");
            Assert.Throws<InvalidOperationException>(() => Read(twoInts, s =>
            {
                s.Int("a", ref a); s.Int("b", ref b); s.Int("c", ref a);
            }), "reading more fields than were written");
        }

        [Test]
        public void DescribeNamesEachValueWithItsSection()
        {
            int score = 7;
            var v = new Vector2(0.5f, -1f);
            byte[] bytes = Write(s =>
            {
                s.BeginSection("Player[3]");
                s.Int("score", ref score);
                s.Vector2("velocity", ref v);
            });

            stream.BeginDescribe(bytes);
            stream.BeginSection("Player[3]");
            int dummy = 0;
            var dummyV = default(Vector2);
            stream.Int("score", ref dummy);
            stream.Vector2("velocity", ref dummyV);
            var described = stream.EndDescribe();

            Assert.That(dummy, Is.EqualTo(0), "Describe must not assign to fields");
            Assert.That(described.Select(p => p.Key + "=" + p.Value), Is.EqualTo(new[]
            {
                "Player[3].score=7", "Player[3].velocity.x=0.5", "Player[3].velocity.y=-1",
            }));
        }

        // ------------------------------------------------------------- the simulated classes

        [Test]
        public void CharacterRoundTrips()
        {
            MakeActiveFly();
            var a = Make<Character>();
            var b = Make<Character>();
            var fresh = Make<Character>();
            AssertRoundTrip(a.SyncState, b.SyncState, fresh.SyncState);
        }

        [Test]
        public void FlyRoundTrips()
        {
            // A reference to a Character is saved as its Player's Slot, so each Character
            // needs its Player, as it has in the game.
            for (int slot = 0; slot < 6; slot++)
            {
                var character = Make<Character>();
                character.player = Player.BySlot(slot);
                Player.BySlot(slot).character = character;
            }

            var a = Make<Fly>();
            var b = Make<Fly>();
            var fresh = Make<Fly>();
            AssertRoundTrip(a.SyncState, b.SyncState, fresh.SyncState);
        }

        [Test]
        public void GameControllerRoundTrips()
        {
            var a = Make<GameController>();
            var b = Make<GameController>();
            var fresh = Make<GameController>();
            AssertRoundTrip(a.SyncState, b.SyncState, fresh.SyncState);
        }

        [Test]
        public void PlayerRoundTrips()
        {
            var a = new Player(FreeLives.InputReader.Device.Keyboard1, Color.white, 0);
            var b = new Player(FreeLives.InputReader.Device.Keyboard1, Color.white, 1);
            var fresh = new Player(FreeLives.InputReader.Device.Keyboard1, Color.white, 2);
            AssertRoundTrip(a.SyncState, b.SyncState, fresh.SyncState);
        }

        [Test]
        public void DescribeNamesEveryCharacterField()
        {
            var character = Make<Character>();
            byte[] bytes = Write(character.SyncState);

            stream.BeginDescribe(bytes);
            stream.BeginSection("Character[0]");
            character.SyncState(stream);
            var described = stream.EndDescribe();

            var names = described.Select(p => p.Key).ToList();
            Assert.That(names, Is.Unique, "every field needs its own name, or a Desync report is ambiguous");
            Assert.That(names, Has.Member("Character[0].position.x"));
            Assert.That(names, Has.Member("Character[0].input.wasAButton"));
            Assert.That(names, Has.Member("Character[0].lastHitByPlayer"));
            Assert.That(names, Has.Member("Character[0].tongueState"));
        }
    }
}
