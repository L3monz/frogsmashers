using System;
using FreeLives;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// One frog's input for one frame, in the form the rollback core wants: small, exactly
    /// comparable, and cheap to put on a wire.
    ///
    /// The stick axes are quantised to bytes rather than kept as floats, and that's not just
    /// a bandwidth choice. Rollback compares a predicted input against the real one to decide
    /// whether to re-simulate; with raw floats, a stick resting at 0.0001 one frame and
    /// 0.0002 the next counts as a different input, so a player merely *holding* a stick
    /// still would trigger a rollback on almost every frame. Quantising makes "the stick
    /// didn't really move" actually compare equal.
    ///
    /// Note what isn't here: the "was this button down last frame" flags that
    /// <see cref="InputState"/> carries. Those are derived from the *previous* frame, which
    /// makes them simulation state, not input — they get saved and restored with everything
    /// else, so a rollback rewinds them correctly too.
    /// </summary>
    [Serializable]
    public struct FrogInput : IEquatable<FrogInput>
    {
        [Flags]
        private enum Buttons : ushort
        {
            None = 0,
            A = 1 << 0,
            B = 1 << 1,
            X = 1 << 2,
            Y = 1 << 3,
            Start = 1 << 4,
            Left = 1 << 5,
            Right = 1 << 6,
            Up = 1 << 7,
            Down = 1 << 8,
        }

        /// <summary>Stick X, quantised to -127..127.</summary>
        public sbyte XAxis;

        /// <summary>Stick Y, quantised to -127..127.</summary>
        public sbyte YAxis;

        private ushort _buttons;

        public bool AButton => Has(Buttons.A);
        public bool BButton => Has(Buttons.B);
        public bool XButton => Has(Buttons.X);
        public bool YButton => Has(Buttons.Y);
        public bool Start => Has(Buttons.Start);

        // The digital directions are stored as their own bits, exactly as the device reported
        // them. They used to be derived from the stick axis, which lost one case: on a
        // keyboard, holding left and right together gives an axis of 0, which read as "left
        // only", while the original input reported both held, and the movement code checks
        // them separately. Gamepads already digitise the stick in InputReader (dead zone 0.3).
        public bool Left => Has(Buttons.Left);
        public bool Right => Has(Buttons.Right);
        public bool Up => Has(Buttons.Up);
        public bool Down => Has(Buttons.Down);

        public float X => XAxis / 127f;
        public float Y => YAxis / 127f;

        /// <summary>Capture the current state of a device into a frame's worth of input.</summary>
        public static FrogInput From(InputState state)
        {
            var input = new FrogInput
            {
                XAxis = Quantise(state.xAxis),
                YAxis = Quantise(state.yAxis),
            };

            input.Set(Buttons.Left, state.left);
            input.Set(Buttons.Right, state.right);
            input.Set(Buttons.Up, state.up);
            input.Set(Buttons.Down, state.down);
            input.Set(Buttons.A, state.aButton);
            input.Set(Buttons.B, state.bButton);
            input.Set(Buttons.X, state.xButton);
            input.Set(Buttons.Y, state.yButton);
            input.Set(Buttons.Start, state.start);
            return input;
        }

        /// <summary>Builds an input directly. For tests and recorded replays.</summary>
        public static FrogInput Make(bool left = false, bool right = false, bool up = false, bool down = false,
            bool a = false, bool b = false, bool x = false, bool y = false, bool start = false)
        {
            var input = new FrogInput();
            input.Set(Buttons.Left, left);
            input.Set(Buttons.Right, right);
            input.Set(Buttons.Up, up);
            input.Set(Buttons.Down, down);
            input.Set(Buttons.A, a);
            input.Set(Buttons.B, b);
            input.Set(Buttons.X, x);
            input.Set(Buttons.Y, y);
            input.Set(Buttons.Start, start);
            return input;
        }

        /// <summary>The whole input as one 32-bit value, for recordings: X, Y, then the buttons.</summary>
        public uint ToBits()
        {
            return (uint)(byte)XAxis | ((uint)(byte)YAxis << 8) | ((uint)_buttons << 16);
        }

        /// <summary>The inverse of <see cref="ToBits"/>.</summary>
        public static FrogInput FromBits(uint bits)
        {
            return new FrogInput
            {
                XAxis = (sbyte)(byte)(bits & 0xFF),
                YAxis = (sbyte)(byte)((bits >> 8) & 0xFF),
                _buttons = (ushort)(bits >> 16),
            };
        }

        private bool Has(Buttons button) => (_buttons & (ushort)button) != 0;

        private void Set(Buttons button, bool pressed)
        {
            if (pressed) _buttons |= (ushort)button;
            else _buttons &= (ushort)~(ushort)button;
        }

        private static sbyte Quantise(float axis)
        {
            return (sbyte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(axis, -1f, 1f) * 127f), -127, 127);
        }

        public bool Equals(FrogInput other) =>
            XAxis == other.XAxis && YAxis == other.YAxis && _buttons == other._buttons;

        public override bool Equals(object obj) => obj is FrogInput other && Equals(other);

        public override int GetHashCode() => (XAxis << 16) ^ (YAxis << 8) ^ _buttons;

        public override string ToString() => $"({XAxis},{YAxis}) buttons={_buttons:X3}";
    }
}
