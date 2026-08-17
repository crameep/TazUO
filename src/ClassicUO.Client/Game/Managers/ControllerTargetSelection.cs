using System.Collections.Generic;

namespace ClassicUO.Game.Managers
{
    /// <summary>Pure selection maths for controller target cycling, kept separate so it is testable.</summary>
    internal static class ControllerTargetSelection
    {
        // Filters offered in cycle order. Party/Followers are deliberately omitted; they are
        // reachable via Mobiles and would make the common hostile/object flip slower.
        private static readonly ScanTypeObject[] _filterOrder =
        {
            ScanTypeObject.Hostile,
            ScanTypeObject.Mobiles,
            ScanTypeObject.Objects
        };

        public static IReadOnlyList<ScanTypeObject> FilterOrder => _filterOrder;

        /// <summary>Next serial in the candidate list, wrapping. Returns 0 when there is nothing to select.</summary>
        /// <remarks>
        /// A current serial that has left the list (died, moved out of range) restarts from the
        /// nearest end rather than losing the input entirely.
        /// </remarks>
        public static uint Cycle(IReadOnlyList<uint> candidates, uint current, int direction)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return 0;
            }

            if (direction == 0)
            {
                return current;
            }

            int index = IndexOf(candidates, current);

            if (index < 0)
            {
                // Selection is stale; step in from whichever end matches the direction travelled.
                return direction > 0 ? candidates[0] : candidates[candidates.Count - 1];
            }

            int step = direction > 0 ? 1 : -1;
            int next = (index + step) % candidates.Count;

            if (next < 0)
            {
                next += candidates.Count;
            }

            return candidates[next];
        }

        /// <summary>Next filter in cycle order, wrapping.</summary>
        public static ScanTypeObject CycleFilter(ScanTypeObject current, int direction)
        {
            int index = IndexOfFilter(current);
            int step = direction >= 0 ? 1 : -1;
            int next = (index + step) % _filterOrder.Length;

            if (next < 0)
            {
                next += _filterOrder.Length;
            }

            return _filterOrder[next];
        }

        private static int IndexOf(IReadOnlyList<uint> candidates, uint serial)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] == serial)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOfFilter(ScanTypeObject filter)
        {
            for (int i = 0; i < _filterOrder.Length; i++)
            {
                if (_filterOrder[i] == filter)
                {
                    return i;
                }
            }

            // Unknown filter (e.g. Party from a macro) enters the cycle at the start.
            return 0;
        }
    }
}
