using System.Collections.Generic;
using System.Linq;
using FrogSmashers.Netcode;
using Hindsight.Core;
using NUnit.Framework;
using UnityEngine;

namespace FrogSmashers.Tests
{
    /// <summary>
    /// The whole Saved State, as Hindsight sees it: FrogSimulation.SaveState and LoadState
    /// over the global section, the Round, the roster, the Players, the Characters and the fly.
    /// </summary>
    [TestFixture]
    public class FrogSimulationTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private FrogSimulation simulation;
        private Player[] players;

        [SetUp]
        public void CreateWorld()
        {
            players = new Player[6];
            for (int slot = 0; slot < 6; slot++)
                players[slot] = new Player(FreeLives.InputReader.Device.Keyboard1, Color.white, slot);
            GameController.activePlayers.Clear();

            var characterPrefab = new GameObject("Character").AddComponent<Character>();
            var flyPrefab = new GameObject("Fly").AddComponent<Fly>();
            var round = new GameObject("GameController").AddComponent<GameController>();
            created.Add(characterPrefab.gameObject);
            created.Add(flyPrefab.gameObject);
            created.Add(round.gameObject);

            SimWorld.CreatePools(characterPrefab, flyPrefab);
            for (int slot = 0; slot < 6; slot++)
                created.Add(SimWorld.Characters.Get(slot).gameObject);
            created.Add(SimWorld.Fly.gameObject);

            simulation = new FrogSimulation(round);
        }

        [TearDown]
        public void DestroyWorld()
        {
            SimWorld.Clear();
            GameController.activePlayers.Clear();
            SimClock.SeekTo(0);
            foreach (var go in created)
                if (go != null)
                    Object.DestroyImmediate(go);
            created.Clear();
        }

        private byte[] Save()
        {
            var buffer = new StateBuffer();
            simulation.SaveState(buffer);
            return buffer.ToArray();
        }

        private void Load(byte[] saved)
        {
            var buffer = new StateBuffer();
            buffer.Write(saved, 0, saved.Length);
            simulation.LoadState(buffer);
        }

        [Test]
        public void SavingIntoAReusedBufferMatchesAFreshOne()
        {
            GameController.activePlayers.Add(players[2]);
            SimWorld.Characters.Spawn(2, new Vector3(1f, 2f, 0f));
            var reused = new StateBuffer(initialCapacity: 8);
            reused.Write(new byte[64], 0, 64);
            reused.Clear();

            simulation.SaveState(reused);

            Assert.That(reused.ToArray(), Is.EqualTo(Save()));
        }

        [Test]
        public void LoadingReadsOnlyTheStateBytes()
        {
            SimClock.SeekTo(77);
            var buffer = new StateBuffer(initialCapacity: 1 << 16);
            simulation.SaveState(buffer);
            SimClock.SeekTo(5);

            simulation.LoadState(buffer);

            Assert.That(SimClock.Tick, Is.EqualTo(77), "a buffer longer than its state must load cleanly");
        }

        [Test]
        public void SavingAndLoadingAllocateNothing()
        {
            GameController.activePlayers.Add(players[0]);
            GameController.activePlayers.Add(players[1]);
            SimWorld.Characters.Spawn(0, new Vector3(1f, 2f, 0f));
            SimWorld.Characters.Spawn(1, new Vector3(-1f, 2f, 0f));
            SimWorld.Flies.Spawn(0, new Vector3(0f, 5f, 0f));
            var buffer = new StateBuffer();
            simulation.SaveState(buffer);
            simulation.LoadState(buffer);

            // The editor does not allow switching the collector off, so collect first, and
            // measure again if a collection ran during the loops (it would hide allocations).
            long saveBytes = 0, loadBytes = 0;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                System.GC.Collect();
                int collections = System.GC.CollectionCount(0);
                long before = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                for (int i = 0; i < 50; i++)
                {
                    buffer.Clear();
                    simulation.SaveState(buffer);
                }
                long mid = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                for (int i = 0; i < 50; i++)
                    simulation.LoadState(buffer);
                long after = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                saveBytes = mid - before;
                loadBytes = after - mid;
                if (System.GC.CollectionCount(0) == collections)
                    break;
            }

            Assert.That(saveBytes + loadBytes, Is.LessThan(1024),
                "50 saves allocated " + saveBytes + " bytes; 50 loads allocated " + loadBytes + " bytes; state is " + buffer.Length + " bytes");
        }

        [Test]
        public void LoadingRestoresTheWholeRound()
        {
            GameController.activePlayers.Add(players[1]);
            GameController.activePlayers.Add(players[3]);
            players[1].score = 4;
            SimWorld.Characters.Spawn(1, new Vector3(2f, 3f, 0f));
            SimWorld.Characters.Spawn(3, new Vector3(-5f, 1f, 0f));
            SimWorld.Flies.Spawn(0, new Vector3(0f, 6f, 0f));
            SimClock.SeekTo(123);
            SimRng.Seed(42);
            byte[] saved = Save();

            // Change everything a Rollback would have to undo.
            GameController.activePlayers.Remove(players[3]);
            GameController.activePlayers.Add(players[5]);
            players[1].score = 9;
            SimWorld.Characters.Despawn(1);
            SimWorld.Characters.Spawn(5, Vector3.zero);
            SimWorld.Characters.Get(3).transform.position = new Vector3(40f, 40f, 0f);
            SimWorld.Flies.Despawn(0);
            SimClock.SeekTo(999);
            SimRng.Range(0f, 1f);

            Load(saved);

            Assert.That(Save(), Is.EqualTo(saved), "a load must restore every byte");
            Assert.That(SimClock.Tick, Is.EqualTo(123));
            Assert.That(players[1].score, Is.EqualTo(4));
            Assert.That(GameController.activePlayers, Is.EqualTo(new[] { players[1], players[3] }));
            Assert.That(SimWorld.Characters.IsAlive(1), Is.True);
            Assert.That(SimWorld.Characters.IsAlive(5), Is.False);
            Assert.That(SimWorld.Characters.Get(3).transform.position, Is.EqualTo(new Vector3(-5f, 1f, 0f)));
            Assert.That(SimWorld.Flies.IsAlive(0), Is.True);
        }

        [Test]
        public void TheRosterIsRebuiltInSlotOrder()
        {
            GameController.activePlayers.Add(players[4]);
            GameController.activePlayers.Add(players[0]);
            byte[] saved = Save();

            GameController.activePlayers.Clear();
            Load(saved);

            Assert.That(GameController.activePlayers, Is.EqualTo(new[] { players[0], players[4] }));
        }

        [Test]
        public void TheRandomStreamIsPartOfTheSavedState()
        {
            SimRng.Seed(7);
            byte[] saved = Save();
            float first = SimRng.Range(0f, 1f);

            Load(saved);

            Assert.That(SimRng.Range(0f, 1f), Is.EqualTo(first), "after a load the stream repeats the same draws");
        }

        [Test]
        public void DescribeNamesEverySection()
        {
            var names = simulation.Describe(Save()).Select(p => p.Key).ToList();

            Assert.That(names, Is.Unique);
            Assert.That(names, Has.Member("Global.tick"));
            Assert.That(names, Has.Member("Global.random"));
            Assert.That(names, Has.Member("Round.state"));
            Assert.That(names, Has.Member("Round.inRoster[3]"));
            Assert.That(names, Has.Member("Player[2].score"));
            Assert.That(names, Has.Member("Character[5].position.x"));
            Assert.That(names, Has.Member("Fly[0].alive"));
        }
    }
}
