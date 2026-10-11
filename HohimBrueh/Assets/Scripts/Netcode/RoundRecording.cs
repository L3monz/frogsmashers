using System;
using System.IO;
using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// One recorded Round, for the replay check: the Round Setup, the checksum of the starting
    /// state, then for every Tick the six inputs and the checksum of the state after the Tick.
    ///
    /// The Round Setup is everything a Round reads at the start that is not in the Saved
    /// State: the Level, the seed, the option flags, the roster in order (GameController gives
    /// spawn delays by roster order), and each Slot's team, round wins and colour.
    ///
    /// Recording reuses its arrays from Round to Round and only grows them, so adding a Tick
    /// allocates nothing. Reading and writing files allocate; they run outside the Round.
    /// </summary>
    public sealed class RoundRecording
    {
        public const int Slots = 6;
        private const uint Magic = 0x50525346; // "FSRP"
        private const int Version = 1;

        // Round Setup.
        public string Level = "";
        public ulong Seed;
        public bool TeamMode, PlayersCanDropIn, ShowDown;
        public bool CharactersBounceEachOther, WeirdBounceTrajectories, OnlyBounceBeforeRecover;
        public int[] Roster = new int[0];
        public readonly Team[] Teams = new Team[Slots];
        public readonly int[] RoundWins = new int[Slots];
        public readonly Color[] Colors = new Color[Slots];

        /// <summary>The frame-rate cap when the Round started (-1 for none). Information only.</summary>
        public int RecordedCap;

        public uint StartChecksum;

        /// <summary>True if the last recorded Tick ended the Round; false if the scene ended first.</summary>
        public bool EndedWithRoundOver;

        public int TickCount { get; private set; }

        private uint[] inputs = new uint[Slots * 60 * 60 * 5];
        private uint[] checksums = new uint[60 * 60 * 5];

        /// <summary>Starts a new recording, keeping the arrays.</summary>
        public void Clear()
        {
            TickCount = 0;
            EndedWithRoundOver = false;
        }

        public void AddTick(FrogInput[] tickInputs, uint checksum)
        {
            if (TickCount == checksums.Length)
            {
                Array.Resize(ref checksums, checksums.Length * 2);
                Array.Resize(ref inputs, inputs.Length * 2);
            }
            for (int slot = 0; slot < Slots; slot++)
                inputs[TickCount * Slots + slot] = tickInputs[slot].ToBits();
            checksums[TickCount] = checksum;
            TickCount++;
        }

        public void GetInputs(int tick, FrogInput[] into)
        {
            for (int slot = 0; slot < Slots; slot++)
                into[slot] = FrogInput.FromBits(inputs[tick * Slots + slot]);
        }

        public uint ChecksumAt(int tick)
        {
            return checksums[tick];
        }

        public void Write(Stream stream)
        {
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(Level);
                w.Write(Seed);
                w.Write(TeamMode);
                w.Write(PlayersCanDropIn);
                w.Write(ShowDown);
                w.Write(CharactersBounceEachOther);
                w.Write(WeirdBounceTrajectories);
                w.Write(OnlyBounceBeforeRecover);
                w.Write(Roster.Length);
                foreach (int slot in Roster)
                    w.Write(slot);
                for (int slot = 0; slot < Slots; slot++)
                {
                    w.Write((int)Teams[slot]);
                    w.Write(RoundWins[slot]);
                    w.Write(Colors[slot].r);
                    w.Write(Colors[slot].g);
                    w.Write(Colors[slot].b);
                    w.Write(Colors[slot].a);
                }
                w.Write(RecordedCap);
                w.Write(StartChecksum);
                w.Write(EndedWithRoundOver);
                w.Write(TickCount);
                for (int i = 0; i < TickCount * Slots; i++)
                    w.Write(inputs[i]);
                for (int i = 0; i < TickCount; i++)
                    w.Write(checksums[i]);
            }
        }

        public static RoundRecording Read(Stream stream)
        {
            using (var r = new BinaryReader(stream))
            {
                if (r.ReadUInt32() != Magic)
                    throw new InvalidDataException("Not a Frog Smashers round recording.");
                int version = r.ReadInt32();
                if (version != Version)
                    throw new InvalidDataException("Round recording version " + version + ", expected " + Version + ".");

                var rec = new RoundRecording();
                rec.Level = r.ReadString();
                rec.Seed = r.ReadUInt64();
                rec.TeamMode = r.ReadBoolean();
                rec.PlayersCanDropIn = r.ReadBoolean();
                rec.ShowDown = r.ReadBoolean();
                rec.CharactersBounceEachOther = r.ReadBoolean();
                rec.WeirdBounceTrajectories = r.ReadBoolean();
                rec.OnlyBounceBeforeRecover = r.ReadBoolean();
                rec.Roster = new int[r.ReadInt32()];
                for (int i = 0; i < rec.Roster.Length; i++)
                    rec.Roster[i] = r.ReadInt32();
                for (int slot = 0; slot < Slots; slot++)
                {
                    rec.Teams[slot] = (Team)r.ReadInt32();
                    rec.RoundWins[slot] = r.ReadInt32();
                    rec.Colors[slot] = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                }
                rec.RecordedCap = r.ReadInt32();
                rec.StartChecksum = r.ReadUInt32();
                rec.EndedWithRoundOver = r.ReadBoolean();
                int ticks = r.ReadInt32();
                rec.inputs = new uint[Math.Max(ticks * Slots, Slots)];
                rec.checksums = new uint[Math.Max(ticks, 1)];
                for (int i = 0; i < ticks * Slots; i++)
                    rec.inputs[i] = r.ReadUInt32();
                for (int i = 0; i < ticks; i++)
                    rec.checksums[i] = r.ReadUInt32();
                rec.TickCount = ticks;
                return rec;
            }
        }
    }
}
