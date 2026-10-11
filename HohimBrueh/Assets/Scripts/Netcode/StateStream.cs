using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Hindsight.Core;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// Saves and loads Simulation State with one symmetric method per class.
    ///
    /// Each simulated class has <c>SyncState(StateStream s)</c>, which lists its fields in a
    /// fixed order: <c>s.Float(nameof(velocityX), ref velocityX)</c>. The same method runs in
    /// every mode:
    /// - <see cref="Mode.Write"/> appends each value to the Saved State.
    /// - <see cref="Mode.Read"/> reads each value back into the field.
    /// - <see cref="Mode.Describe"/> reads each value and records <c>name = value</c>, without
    ///   touching any field. The Desync report uses it to name the field that differs.
    /// - <see cref="Mode.Fill"/> assigns generated values that differ from typical defaults:
    ///   bools become true, numbers become distinct non-zero values, Slots become valid.
    ///   Tests use it to check that every field is restored: a field that Fill could not
    ///   change is a field whose value SyncState never assigns back.
    ///
    /// Rules for <c>SyncState</c>:
    /// - No branches on state. Every call writes the same fields in the same order, so every
    ///   Saved State has the same layout and Describe can decode any of them.
    /// - A property, a transform value or an enum: copy it to a local, sync the local, and
    ///   assign it back only <c>if (s.IsReading)</c>.
    /// - A reference: convert it to a Slot number with <see cref="Slot"/>, and convert it back
    ///   only <c>if (s.IsReading)</c>.
    ///
    /// The format is little-endian bytes written by hand, so it is the same on every machine.
    /// Floats keep their exact IEEE 754 bits. Write reuses one buffer. Ending a write into a
    /// <see cref="StateBuffer"/> allocates nothing; the overload that returns an array
    /// allocates it. Read allocates nothing; Describe allocates its list.
    /// </summary>
    public sealed class StateStream
    {
        public enum Mode { Write, Read, Describe, Fill }

        // netstandard2.0 has no BitConverter.SingleToInt32Bits, and a union reads the bits
        // without boxing or unsafe code.
        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] public float Float;
            [FieldOffset(0)] public int Int;
        }

        private byte[] buffer = new byte[4096];
        private byte[] source;
        private int sourceLength;
        private int position;
        private int fillCounter;
        private string section = "";
        private List<KeyValuePair<string, string>> described;

        public Mode CurrentMode { get; private set; }

        /// <summary>True when the stream assigns values to fields: in Read and Fill modes.</summary>
        public bool IsReading
        {
            get { return CurrentMode == Mode.Read || CurrentMode == Mode.Fill; }
        }

        public void BeginWrite()
        {
            CurrentMode = Mode.Write;
            position = 0;
            section = "";
        }

        /// <summary>Returns the Saved State written since <see cref="BeginWrite"/>, as a new array.</summary>
        public byte[] EndWrite()
        {
            var result = new byte[position];
            Buffer.BlockCopy(buffer, 0, result, 0, position);
            return result;
        }

        /// <summary>Appends the Saved State written since <see cref="BeginWrite"/> to <paramref name="into"/>.</summary>
        public void EndWrite(StateBuffer into)
        {
            into.Write(buffer, 0, position);
        }

        public void BeginRead(byte[] state)
        {
            BeginRead(state, state.Length);
        }

        /// <summary>Reads the first <paramref name="length"/> bytes of <paramref name="state"/>.</summary>
        public void BeginRead(byte[] state, int length)
        {
            CurrentMode = Mode.Read;
            source = state;
            sourceLength = length;
            position = 0;
            section = "";
        }

        /// <summary>
        /// Checks that the read used exactly the whole Saved State. A difference means that
        /// SyncState wrote a different set of fields than it read, which is a bug in the
        /// layout, not in the game.
        /// </summary>
        public void EndRead()
        {
            if (position != sourceLength)
                throw new InvalidOperationException(
                    "Saved State layout mismatch: read " + position + " of " + sourceLength + " bytes.");
            source = null;
        }

        public void BeginDescribe(byte[] state)
        {
            CurrentMode = Mode.Describe;
            source = state;
            sourceLength = state.Length;
            position = 0;
            section = "";
            described = new List<KeyValuePair<string, string>>();
        }

        /// <summary>Returns every field in the Saved State as a <c>path = value</c> pair, in order.</summary>
        public List<KeyValuePair<string, string>> EndDescribe()
        {
            EndRead();
            var result = described;
            described = null;
            return result;
        }

        /// <summary>Starts assigning generated values. For tests only.</summary>
        public void BeginFill(int seed)
        {
            CurrentMode = Mode.Fill;
            fillCounter = seed;
            section = "";
        }

        /// <summary>Prefixes the names that follow, for example <c>Character[2]</c>.</summary>
        public void BeginSection(string name)
        {
            section = name;
        }

        public void Bool(string name, ref bool value)
        {
            switch (CurrentMode)
            {
                case Mode.Write: Put(value ? 1 : 0, 1); break;
                case Mode.Read: value = Take(1) != 0; break;
                case Mode.Fill: value = true; NextFill(); break;
                case Mode.Describe: Record(name, Take(1) != 0 ? "true" : "false"); break;
            }
        }

        public void Int(string name, ref int value)
        {
            switch (CurrentMode)
            {
                case Mode.Write: Put(value, 4); break;
                case Mode.Read: value = (int)Take(4); break;
                case Mode.Fill: value = NextFill(); break;
                case Mode.Describe: Record(name, ((int)Take(4)).ToString(CultureInfo.InvariantCulture)); break;
            }
        }

        /// <summary>
        /// A Slot number: 0 to 5, or -1 for none. Stored like an int. Fill mode generates only
        /// valid Slots, so a test can resolve every reference.
        /// </summary>
        public void Slot(string name, ref int slot)
        {
            if (CurrentMode == Mode.Fill)
            {
                slot = NextFill() % 6;
                return;
            }
            Int(name, ref slot);
        }

        public void Float(string name, ref float value)
        {
            var bits = new FloatBits();
            switch (CurrentMode)
            {
                case Mode.Write:
                    bits.Float = value;
                    Put(bits.Int, 4);
                    break;
                case Mode.Read:
                    bits.Int = (int)Take(4);
                    value = bits.Float;
                    break;
                case Mode.Fill:
                    value = NextFill() * 0.25f + 0.125f;
                    break;
                case Mode.Describe:
                    bits.Int = (int)Take(4);
                    // "R" round-trips, so two values that print the same are bit-identical.
                    Record(name, bits.Float.ToString("R", CultureInfo.InvariantCulture));
                    break;
            }
        }

        public void ULong(string name, ref ulong value)
        {
            switch (CurrentMode)
            {
                case Mode.Write:
                    Put((int)(value & 0xFFFFFFFF), 4);
                    Put((int)(value >> 32), 4);
                    break;
                case Mode.Read:
                    value = Take(4) | (Take(4) << 32);
                    break;
                case Mode.Fill:
                    value = (ulong)NextFill() * 0x9E3779B97F4A7C15UL;
                    break;
                case Mode.Describe:
                    Record(name, (Take(4) | (Take(4) << 32)).ToString(CultureInfo.InvariantCulture));
                    break;
            }
        }

        // The component paths ("position.x") are only used to describe a state. Building them
        // on every save and load made a few dozen strings of garbage per Saved State.
        public void Vector2(string name, ref Vector2 value)
        {
            bool describing = CurrentMode == Mode.Describe;
            Float(describing ? name + ".x" : name, ref value.x);
            Float(describing ? name + ".y" : name, ref value.y);
        }

        public void Vector3(string name, ref Vector3 value)
        {
            bool describing = CurrentMode == Mode.Describe;
            Float(describing ? name + ".x" : name, ref value.x);
            Float(describing ? name + ".y" : name, ref value.y);
            Float(describing ? name + ".z" : name, ref value.z);
        }

        private void Put(int value, int bytes)
        {
            if (position + bytes > buffer.Length)
                Array.Resize(ref buffer, buffer.Length * 2);
            for (int i = 0; i < bytes; i++)
                buffer[position++] = (byte)(value >> (8 * i));
        }

        private ulong Take(int bytes)
        {
            if (position + bytes > sourceLength)
                throw new InvalidOperationException(
                    "Saved State layout mismatch: read past the end of " + sourceLength + " bytes.");
            ulong value = 0;
            for (int i = 0; i < bytes; i++)
                value |= (ulong)source[position++] << (8 * i);
            return value;
        }

        private int NextFill()
        {
            return ++fillCounter;
        }

        private void Record(string name, string value)
        {
            string path = section.Length == 0 ? name : section + "." + name;
            described.Add(new KeyValuePair<string, string>(path, value));
        }
    }
}
