using System.Collections;
using System.Collections.Generic;
using FrogSmashers.Netcode;
using Hindsight.Determinism;
using UnityEngine;

public class Fly : MonoBehaviour, ISimulated
{
    Vector2 velocity, targetVelocity;
    int terrainLayer;
    public SpriteRenderer sprite;

    public float maxSpeed;
    internal bool BeingIngested;
    internal Character ingestedBy;

    // Safety timeout: even with a valid owner, force release if a fly stays
    // claimed for longer than this. Prevents pathological "stuck fly" states.
    const float ingestTimeoutDuration = 3f;
    float ingestTimeout;

    float updateDirectionDelay;

    // Awake, not Start. Unity calls Awake during Instantiate, and GameController
    // instantiates the fly inside a tick. Thus this draw happens at the same point in the
    // tick on every machine. Unity calls Start later, from the frame loop, so the draw order
    // would depend on frame timing. Also, a tick can run before Start, and the fly would
    // simulate with no terrain layer and no velocity.
    void Awake()
    {
        terrainLayer = 1 << LayerMask.NameToLayer("Ground");
        velocity = SimRng.Direction() * 10f;
    }

    void OnEnable()
    {
        SimulationDriver.Register(this);
    }

    void OnDisable()
    {
        SimulationDriver.Unregister(this);
    }

    internal bool TryClaim(Character claimant)
    {
        if (ingestedBy != null && ingestedBy != claimant)
            return false;
        ingestedBy = claimant;
        BeingIngested = true;
        ingestTimeout = ingestTimeoutDuration;
        return true;
    }

    internal void Release()
    {
        ingestedBy = null;
        BeingIngested = false;
        ingestTimeout = 0f;
    }

    /// <summary>
    /// Advance this fly by exactly one tick.
    ///
    /// Split out of Update() for the same reason Character was: the driver owns "advance one
    /// tick" so a rollback can re-run past ticks on demand, which is impossible while each
    /// object ticks itself off the frame loop.
    /// </summary>
    public void SimulateTick(float deltaTime)
    {

        updateDirectionDelay -= deltaTime;
        if (updateDirectionDelay < 0f)
        {
            updateDirectionDelay = SimRng.Range(3f, 10f);
            UpdateDirection();
        }

        if (BeingIngested)
        {
            // Watchdog: the owner must still exist, be active, and still point at us.
            // Otherwise the fly is orphaned (e.g. owner died, got knocked, or another
            // tongue raced past the claim check on an older code path) — release it.
            bool ownerLost = ingestedBy == null
                             || !ingestedBy.gameObject.activeInHierarchy
                             || ingestedBy.ingestingFly != this;
            ingestTimeout -= deltaTime;
            if (ownerLost || ingestTimeout <= 0f)
                Release();
        }

        if (!BeingIngested)
            RunMotion(deltaTime);

        if (transform.position.x < Terrain.LeftKillPoint || transform.position.x > Terrain.RightKillPoint || transform.position.y > Terrain.TopKillPoint || transform.position.y < Terrain.BotKillPoint)
            Destroy(gameObject);

    }

    void RunMotion(float deltaTime)
    {
        velocity = Vector2.MoveTowards(velocity, targetVelocity, 5f * deltaTime);

        // The bob used to read Time.time, which is wall-clock and therefore different every
        // time a tick gets replayed. SimClock.Elapsed is derived from the tick number instead,
        // so tick N always produces the same bob. SimMath.Sin rather than Mathf.Sin because
        // IEEE 754 doesn't specify sine — platform math libraries disagree in the last bit,
        // and in a rollback simulation that compounds into a desync (SMA-20).
        float bob = SimMath.Sin(SimClock.Elapsed * 8f) * 3f;

        Vector2 velocityT = velocity * deltaTime + Vector2.up * bob * deltaTime;
        if (velocityT.x < 0)
        {
            if (Physics2D.Raycast(transform.position, Vector2.left, Mathf.Abs(velocityT.x) + 1f, terrainLayer))
            {
                velocityT.x = 0;
                velocity.x *= -0.5f;
                UpdateDirection();
            }
        }
        if (velocityT.x > 0)
        {
            if (Physics2D.Raycast(transform.position, Vector2.right, Mathf.Abs(velocityT.x) + 1f, terrainLayer))
            {
                velocityT.x = 0;
                velocity.x *= -0.5f;
                UpdateDirection();
            }
        }
        if (velocityT.y < 0)
        {
            if (Physics2D.Raycast(transform.position, Vector2.down, Mathf.Abs(velocityT.y) + 1f, terrainLayer))
            {
                velocityT.y = 0;
                velocity.y *= -0.5f;
                UpdateDirection();
            }
        }
        if (velocityT.y > 0)
        {
            if (Physics2D.Raycast(transform.position, Vector2.up, Mathf.Abs(velocityT.y) + 1f, terrainLayer))
            {
                velocityT.y = 0;
                velocity.y *= -0.5f;
                UpdateDirection();
            }
        }

        if (velocityT.x < 0)
            sprite.transform.localScale = new Vector3(1f, 1f, 1f);
        else
            sprite.transform.localScale = new Vector3(-1f, 1f, 1f);

        transform.position += (Vector3)velocityT;
    }

    private void UpdateDirection()
    {
        if (SimRng.Value < 0.1f)
        {
            targetVelocity = Vector2.zero;
            updateDirectionDelay = SimRng.Range(1f, 3f);
        }
        else
            targetVelocity = SimRng.Direction() * maxSpeed;
    }
}
