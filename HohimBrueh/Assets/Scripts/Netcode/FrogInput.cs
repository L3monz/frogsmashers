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
        private enum Buttons : byte
        {
            None = 0,
            A = 1 << 0,
            B = 1 << 1,
            X = 1 << 2,
            Y = 1 << 3,
            Start = 1 << 4,
        }

        /// <summary>Stick X, quantised to -127..127.</summary>
        public sbyte XAxis;

        /// <summary>Stick Y, quantised to -127..127.</summary>
        public sbyte YAxis;

        private byte _buttons;

        public bool AButton => (_buttons & (byte)Buttons.A) != 0;
        public bool BButton => (_buttons & (byte)Buttons.B) != 0;
        public bool XButton => (_buttons & (byte)Buttons.X) != 0;
        public bool YButton => (_buttons & (byte)Buttons.Y) != 0;
        public bool Start => (_buttons & (byte)Buttons.Start) != 0;

        // The digital directions the game reads are derived from the stick rather than stored
        // separately, so the two can never disagree after a rollback.
        private const sbyte DirectionThreshold = 64; // half deflection

        public bool Left => XAxis <= -DirectionThreshold;
        public bool Right => XAxis >= DirectionThreshold;
        public bool Down => YAxis <= -DirectionThreshold;
        public bool Up => YAxis >= DirectionThreshold;

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

            // Fall back to the digital d-pad when the stick is centred, so keyboard players
            // (who have no analog axis at all) still register as holding a direction.
            if (input.XAxis == 0)
            {
                if (state.left) input.XAxis = -127;
                else if (state.right) input.XAxis = 127;
            }
            if (input.YAxis == 0)
            {
                if (state.down) input.YAxis = -127;
                else if (state.up) input.YAxis = 127;
            }

            input.Set(Buttons.A, state.aButton);
            input.Set(Buttons.B, state.bButton);
            input.Set(Buttons.X, state.xButton);
            input.Set(Buttons.Y, state.yButton);
            input.Set(Buttons.Start, state.start);
            return input;
        }

        private void Set(Buttons button, bool pressed)
        {
            if (pressed) _buttons |= (byte)button;
            else _buttons &= (byte)~(byte)button;
        }

        private static sbyte Quantise(float axis)
        {
            return (sbyte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(axis, -1f, 1f) * 127f), -127, 127);
        }

        public bool Equals(FrogInput other) =>
            XAxis == other.XAxis && YAxis == other.YAxis && _buttons == other._buttons;

        public override bool Equals(object obj) => obj is FrogInput other && Equals(other);

        public override int GetHashCode() => (XAxis << 16) ^ (YAxis << 8) ^ _buttons;

        public override string ToString() => $"({XAxis},{YAxis}) buttons={_buttons:X2}";
    }
}
