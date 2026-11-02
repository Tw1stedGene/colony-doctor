using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ColonyDoctor.Diagnosis
{
    /// <summary>
    /// Per-map memo of reachability between regions for a pawn. Region ids change whenever the region graph is rebuilt,
    /// so stale entries stop matching on their own; the whole cache is also cleared every sampling cycle (250 ticks) to
    /// pick up door and danger changes that don't rebuild regions.
    /// </summary>
    public sealed class ReachCache
    {
        private readonly Dictionary<Key, bool> cache = new Dictionary<Key, bool>(256);
        private readonly Map map;

        public ReachCache(Map map) => this.map = map;

        public void Clear() => cache.Clear();

        /// <summary>Can <paramref name="pawn"/> get from <paramref name="from"/> to <paramref name="target"/>?</summary>
        public bool CanReach(Pawn pawn, IntVec3 from, Thing target, PathEndMode mode)
        {
            Region a = from.GetRegion(map);
            Region b = target.Position.GetRegion(map);
            if (a == null || b == null) return Compute(pawn, from, target, mode);

            var key = new Key(a.id, b.id, pawn.thingIDNumber, (byte)mode);
            if (cache.TryGetValue(key, out bool result)) return result;
            result = Compute(pawn, from, target, mode);
            cache[key] = result;
            return result;
        }

        private bool Compute(Pawn pawn, IntVec3 from, Thing target, PathEndMode mode)
            => map.reachability.CanReach(from, target, mode, TraverseParms.For(pawn, Danger.Some));

        private readonly struct Key : IEquatable<Key>
        {
            private readonly int a, b, pawn;
            private readonly byte mode;

            public Key(int a, int b, int pawn, byte mode)
            {
                this.a = a;
                this.b = b;
                this.pawn = pawn;
                this.mode = mode;
            }

            public bool Equals(Key o) => a == o.a && b == o.b && pawn == o.pawn && mode == o.mode;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => ((a * 397) ^ b) * 397 ^ pawn ^ (mode << 28);
        }
    }
}
