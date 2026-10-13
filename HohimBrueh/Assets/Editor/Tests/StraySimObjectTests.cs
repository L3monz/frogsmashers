using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FrogSmashers.Netcode;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace FrogSmashers.Tests
{
    /// <summary>
    /// Only the pooled Characters and the pooled fly are saved and loaded. A Fly or Character
    /// placed in a scene is outside the Saved State, yet the simulation can still touch it: a
    /// tongue caught the fly placed in Finale, and the Peer that rolled back then moved the
    /// pooled fly instead, which desynced the first internet playtest (SMA-51).
    /// </summary>
    [TestFixture]
    public class StraySimObjectTests
    {
        private readonly List<GameObject> created = new List<GameObject>();

        [SetUp]
        public void CreatePlayers()
        {
            // CreatePools gives each Character its Slot's Player.
            for (int slot = 0; slot < 6; slot++)
                new Player(FreeLives.InputReader.Device.Keyboard1, Color.white, slot);
        }

        [TearDown]
        public void DestroyObjects()
        {
            SimWorld.Clear();
            foreach (var go in created)
                if (go != null)
                    Object.DestroyImmediate(go);
            created.Clear();
        }

        // Every Level a Match loads, and the Showdown.
        private static IEnumerable<string> Levels() => GameController.levelNames.Concat(new[] { "7Showdown" });

        [TestCaseSource(nameof(Levels))]
        public void ALevelHasNoPlacedFlyOrCharacter(string level)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + level + ".unity", OpenSceneMode.Additive);
            try
            {
                var placed = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Fly>(true).Cast<Component>()
                        .Concat(root.GetComponentsInChildren<Character>(true)))
                    .Select(c => c.GetType().Name + " '" + c.name + "'")
                    .ToList();
                Assert.That(placed, Is.Empty, "the pools create these; one placed in the scene is never saved");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void AStrayFlyIsReported()
        {
            CreatePools();
            var stray = new GameObject("Placed fly");
            created.Add(stray);
            stray.AddComponent<Fly>();

            LogAssert.Expect(LogType.Error, new Regex(@"^\[SIM\] stray Fly 'Placed fly'"));
            SimWorld.ReportStrayObjects();
        }

        [Test]
        public void ThePooledObjectsAreNotReported()
        {
            CreatePools();
            SimWorld.ReportStrayObjects();
            LogAssert.NoUnexpectedReceived();
        }

        // Creates the pools from bare stand-in prefabs, then removes the stand-ins: in an
        // edit-mode test they sit in the open scene, where the report would count them.
        private void CreatePools()
        {
            var characterPrefab = new GameObject("Character").AddComponent<Character>();
            var flyPrefab = new GameObject("Fly").AddComponent<Fly>();
            SimWorld.CreatePools(characterPrefab, flyPrefab);
            Object.DestroyImmediate(characterPrefab.gameObject);
            Object.DestroyImmediate(flyPrefab.gameObject);

            for (int slot = 0; slot < 6; slot++)
                created.Add(SimWorld.Characters.Get(slot).gameObject);
            created.Add(SimWorld.Fly.gameObject);
        }
    }
}
