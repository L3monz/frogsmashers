using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TimeController : MonoBehaviour
{

    static TimeController instance;

    // Use this for initialization
    void Awake()
    {
        instance = this;
    }

    // Update is called once per frame
    void Update()
    {
        // Debug slow-motion. This is safe to keep on Unity's global timeScale now that the
        // simulation runs on a fixed tick: scaling time only changes how often
        // SimulationDriver gets to run a tick, never how far a tick advances anything. It
        // makes the game play back slowly without altering a single simulated value.
        if (Input.GetKeyDown(KeyCode.F1))
        {
            if (Time.timeScale > 0.5f)
            {
                Time.timeScale = 0.1f;
            }
            else
            {
                Time.timeScale = 1f;
            }
        }
    }

    /// <summary>
    /// Hit-stop everyone within <paramref name="radius"/> of a hit, scaled by how close they
    /// were.
    ///
    /// This used to have a sibling that put the whole game into slow motion by setting
    /// Time.timeScale, which had to go (SMA-21). Two problems with it: it scaled
    /// Time.deltaTime, so a gameplay event was reaching in and changing the simulation's
    /// timestep, and it was global — landing a hit slowed down the frog of whoever you hit it
    /// on, and their opponent, and everything else. This version was always the better one
    /// anyway: it hands each character its own hit-stop, which is ordinary per-character state
    /// that saves and restores with everything else.
    /// </summary>
    public static void TimeBumpCharacters(Vector2 center, float durationM, float radius, bool dropOff)
    {
        foreach (var player in GameController.activePlayers)
        {
            if (player.character != null)
            {
                float dist = Vector2.Distance(center, player.character.transform.position);
                if (dist < radius)
                {
                    float m = dist / radius;
                    player.character.TimeBump(durationM,dropOff ? m * m : 0f);
                }
            }
        }
    }
}
