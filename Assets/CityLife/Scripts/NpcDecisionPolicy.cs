using System;
using System.Collections.Generic;

namespace CityLife.World
{
    // Pure, deterministic ranking. Only the supplied perceptions can become goals.
    public static class NpcDecisionPolicy
    {
        public static NpcObservation Choose(IReadOnlyList<NpcObservation> visible, bool carrying,
            IReadOnlyDictionary<string, int> retryAfterTick, int tick)
        {
            NpcObservation best = null;
            foreach (var candidate in visible)
            {
                if (candidate.kind != (carrying ? NpcObjectKind.Destination : NpcObjectKind.Item) ||
                    !candidate.permission || !candidate.available) continue;
                if (retryAfterTick.TryGetValue(candidate.id, out int retry) && retry > tick) continue;
                if (best == null || candidate.distanceMillimetres < best.distanceMillimetres ||
                    (candidate.distanceMillimetres == best.distanceMillimetres &&
                     string.Compare(candidate.id, best.id, StringComparison.Ordinal) < 0))
                    best = candidate;
            }
            return best;
        }
    }
}
