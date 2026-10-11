using FrogSmashers.Netcode;
using NUnit.Framework;
using UnityEngine;

namespace FrogSmashers.Tests
{
    /// <summary>The input path: FrogInput, TickInput, and the input a Character gets when it Spawns.</summary>
    [TestFixture]
    public class InputPathTests
    {
        private readonly System.Collections.Generic.List<GameObject> created = new System.Collections.Generic.List<GameObject>();

        [SetUp]
        public void CreateWorld()
        {
            for (int slot = 0; slot < 6; slot++)
                new Player(FreeLives.InputReader.Device.Keyboard1, Color.white, slot);

            var characterPrefab = new GameObject("Character").AddComponent<Character>();
            var flyPrefab = new GameObject("Fly").AddComponent<Fly>();
            created.Add(characterPrefab.gameObject);
            created.Add(flyPrefab.gameObject);
            SimWorld.CreatePools(characterPrefab, flyPrefab);
            for (int slot = 0; slot < 6; slot++)
                created.Add(SimWorld.Characters.Get(slot).gameObject);
            created.Add(SimWorld.Fly.gameObject);
        }

        [TearDown]
        public void DestroyWorld()
        {
            SimWorld.Clear();
            foreach (var go in created)
                if (go != null)
                    Object.DestroyImmediate(go);
            created.Clear();
        }

        private static FrogInput[] AllSlots(FrogInput input)
        {
            var inputs = new FrogInput[6];
            for (int i = 0; i < 6; i++)
                inputs[i] = input;
            return inputs;
        }

        [Test]
        public void FrogInputKeepsLeftAndRightHeldTogether()
        {
            // On a keyboard, left and right together give an axis of 0. The old FrogInput
            // derived directions from the axis and reported "left only".
            var state = new FreeLives.InputState { left = true, right = true, xAxis = 0f, up = true };
            var input = FrogInput.From(state);

            Assert.That(input.Left, Is.True);
            Assert.That(input.Right, Is.True);
            Assert.That(input.Up, Is.True);
            Assert.That(input.Down, Is.False);
        }

        [Test]
        public void FrogInputCarriesEveryButton()
        {
            var state = new FreeLives.InputState { aButton = true, bButton = false, xButton = true, yButton = false, start = true };
            var input = FrogInput.From(state);

            Assert.That(new[] { input.AButton, input.BButton, input.XButton, input.YButton, input.Start },
                Is.EqualTo(new[] { true, false, true, false, true }));
            Assert.That(input, Is.EqualTo(FrogInput.Make(a: true, x: true, start: true)));
        }

        [Test]
        public void TickInputGoesToAliveCharactersOnly()
        {
            var alive = SimWorld.Characters.Spawn(2, Vector3.zero);
            var dead = SimWorld.Characters.Get(3);

            TickInput.Apply(AllSlots(FrogInput.Make(a: true, left: true)));

            Assert.That(alive.input.aButton, Is.True);
            Assert.That(alive.input.left, Is.True);
            Assert.That(dead.input.aButton, Is.False, "a dead Character does not take input");
        }

        [Test]
        public void ApplyingInputRollsThePreviousInputForward()
        {
            var character = SimWorld.Characters.Spawn(0, Vector3.zero);

            TickInput.Apply(AllSlots(FrogInput.Make(a: true)));
            TickInput.Apply(AllSlots(FrogInput.Make()));

            Assert.That(character.input.aButton, Is.False);
            Assert.That(character.input.wasAButton, Is.True, "the previous Tick's press is kept for edge detection");
        }

        [Test]
        public void HoldingAButtonWhileSpawningIsNotANewPress()
        {
            TickInput.Apply(AllSlots(FrogInput.Make(a: true)));
            var character = SimWorld.Characters.Spawn(1, Vector3.zero);

            Assert.That(character.input.aButton, Is.True);
            Assert.That(character.input.wasAButton, Is.True, "previous equals current at Spawn");
        }
    }
}
