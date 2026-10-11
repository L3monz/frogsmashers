using UnityEngine;

namespace FrogSmashers.Netcode
{
    /// <summary>
    /// A component that must reset itself each time its object Spawns. The pool calls
    /// <see cref="OnSpawn"/> on the Tick of the Spawn, after the object is active.
    ///
    /// Simulation code (Fly's first direction) and view code (CharacterAnimator's animation
    /// state) both use it. View resets matter because a pooled object keeps its view
    /// components between lives, where a new instance used to start fresh.
    /// </summary>
    public interface ISpawnable
    {
        void OnSpawn();
    }

    /// <summary>
    /// A pooled object that can be alive but hidden: the fly while a Character holds it in its
    /// mouth. The pool keeps a hidden object inactive, so it does not tick and cannot be hit.
    /// </summary>
    public interface IHideable
    {
        bool Hidden { get; }
    }

    /// <summary>
    /// A fixed number of simulated objects, one per slot, created once at Round start and
    /// reused. During a Round, nothing creates or destroys a simulated
    /// object: Spawn and Despawn change an alive flag, and the alive flags are Simulation
    /// State, so a Rollback can undo them.
    ///
    /// Spawn resets an object by loading a spawn template: the Saved State of a fresh,
    /// never-simulated instance, captured when the pool is created. Every synced field gets
    /// its starting value back, with no reset code to keep in step with the fields.
    ///
    /// The pool registers its objects with the driver once, in slot order, and they stay
    /// registered while dead; the driver skips inactive objects. Registering in OnEnable, as
    /// before, would change the tick order every time something Spawned.
    /// </summary>
    public sealed class SimPool<T> where T : Component, ISimulated
    {
        private readonly T[] items; // sim-state: exclude the pooled objects, created once
        private readonly bool[] alive;
        private readonly ISpawnable[][] spawnables; // sim-state: exclude component lookup, cached at creation
        private readonly string[] sections; // sim-state: exclude Describe section names, built once
        private readonly byte[] template; // sim-state: exclude spawn template, captured once at creation
        private readonly StateStream templateStream = new StateStream();

        /// <param name="prefab">The prefab to copy. It may be active; copies are made inactive without running Awake.</param>
        /// <param name="count">How many slots. Characters use six, one per Player Slot; the fly uses one.</param>
        /// <param name="name">Names the objects and the Describe sections, for example "Character" gives "Character[2]".</param>
        public SimPool(T prefab, int count, string name)
        {
            items = new T[count];
            alive = new bool[count];
            spawnables = new ISpawnable[count][];
            sections = new string[count];

            // Instantiate under an inactive parent, so Awake and OnEnable do not run yet. An
            // object's first activation (its first Spawn, or a load that makes it alive) runs
            // Awake, which may only set configuration (layer masks), never state.
            var holder = new GameObject(name + " pool holder");
            holder.SetActive(false);
            for (int i = 0; i < count; i++)
            {
                var item = Object.Instantiate(prefab, holder.transform);
                item.gameObject.name = name + " " + i;
                item.gameObject.SetActive(false);
                item.transform.SetParent(null, true);
                items[i] = item;
                spawnables[i] = item.GetComponentsInChildren<ISpawnable>(true);
                sections[i] = name + "[" + i + "]";
                SimulationDriver.Register(item);
            }
            if (Application.isPlaying)
                Object.Destroy(holder);
            else
                Object.DestroyImmediate(holder);

            templateStream.BeginWrite();
            items[0].SyncState(templateStream);
            template = templateStream.EndWrite();
        }

        public int Count
        {
            get { return items.Length; }
        }

        /// <summary>The object in a slot, alive or not.</summary>
        public T Get(int slot)
        {
            return items[slot];
        }

        public bool IsAlive(int slot)
        {
            return alive[slot];
        }

        /// <summary>
        /// Brings the slot's object into play at <paramref name="position"/>, reset to its
        /// starting state. Spawning a slot that is already alive resets it in place.
        /// </summary>
        public T Spawn(int slot, Vector3 position)
        {
            var item = items[slot];
            templateStream.BeginRead(template);
            item.SyncState(templateStream);
            templateStream.EndRead();

            item.transform.position = position;
            alive[slot] = true;
            ApplyVisibility(slot);

            var hooks = spawnables[slot];
            for (int i = 0; i < hooks.Length; i++)
                hooks[i].OnSpawn();
            return item;
        }

        /// <summary>Takes the slot's object out of play. It stops ticking at once, on this Tick.</summary>
        public void Despawn(int slot)
        {
            alive[slot] = false;
            ApplyVisibility(slot);
        }

        /// <summary>Saves or loads each slot's alive flag and its object's state, in slot order.</summary>
        public void SyncState(StateStream s)
        {
            for (int i = 0; i < items.Length; i++)
            {
                s.BeginSection(sections[i]);
                bool isAlive = alive[i];
                s.Bool(nameof(alive), ref isAlive);
                if (s.IsReading)
                    alive[i] = isAlive;

                items[i].SyncState(s);

                if (s.IsReading)
                    ApplyVisibility(i);
            }
        }

        /// <summary>Active means alive and not hidden. Calls SetActive only on a change.</summary>
        private void ApplyVisibility(int slot)
        {
            var item = items[slot];
            var hideable = item as IHideable;
            bool visible = alive[slot] && (hideable == null || !hideable.Hidden);
            if (item.gameObject.activeSelf != visible)
                item.gameObject.SetActive(visible);
        }
    }
}
