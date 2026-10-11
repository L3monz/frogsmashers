using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FrogSmashers.Netcode;

public enum Team
{
    Blue,
    Red
}

public class Player
{
    // Looks up a Player by Slot. A Player's sortPriority is its Slot (0-5), so references
    // between simulated objects can be saved as a Slot number and resolved on load.
    static readonly Player[] bySlot = new Player[6]; // sim-state: exclude lookup from Slot to Player, filled by the constructor

    public FreeLives.InputReader.Device inputDevice; // sim-state: exclude Round Setup, set by the constructor

    public Team team;

    public Color color; // sim-state: exclude cosmetic

    public int score;

    public int roundWins;

    public SpriteRenderer offscreenDot; // sim-state: exclude view

    public int sortPriority; // sim-state: exclude the Slot, set by the constructor

    public Player(FreeLives.InputReader.Device inputDevice, Color color, int sortPriority)
    {
        this.inputDevice = inputDevice;
        this.color = color;
        this.sortPriority = sortPriority;
        if (sortPriority >= 0 && sortPriority < bySlot.Length)
            bySlot[sortPriority] = this;
    }

    /// <summary>
    /// This Player's Character while it is alive, otherwise null. Always the pooled Character
    /// in this Player's Slot (spec D5). A property, not a field, so the many existing
    /// "character == null" checks keep meaning "no live Character".
    /// </summary>
    public Character character
    {
        get
        {
            var pool = SimWorld.Characters;
            return pool != null && pool.IsAlive(sortPriority) ? pool.Get(sortPriority) : null;
        }
    }

    public float spawnDelay = 0f;

    /// <summary>The Player in a Slot, or null for -1 or an empty Slot.</summary>
    public static Player BySlot(int slot)
    {
        return slot >= 0 && slot < bySlot.Length ? bySlot[slot] : null;
    }

    /// <summary>The Slot of a Player, or -1 for null.</summary>
    public static int SlotOf(Player player)
    {
        return player == null ? -1 : player.sortPriority;
    }

    /// <summary>
    /// The part of a Player that changes during a Round. Roster membership is synced by
    /// FrogSimulation, which owns the roster (P6). Team, device and Slot are Round Setup;
    /// roundWins is a Match total.
    /// </summary>
    public void SyncState(StateStream s)
    {
        s.Int(nameof(score), ref score);
        s.Float(nameof(spawnDelay), ref spawnDelay);
    }
}
