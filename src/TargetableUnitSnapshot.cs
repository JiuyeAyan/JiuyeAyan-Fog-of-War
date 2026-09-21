using System;
using System.Collections.Generic;

namespace SCDEFogOfWar
{
    internal static class TargetableUnitSnapshot
    {
        public static int[] Create(IEnumerable<int> objectIds)
        {
            if (objectIds == null) return new int[0];
            return CreateIfChanged(new List<int>(objectIds), null);
        }

        public static int[] CreateIfChanged(List<int> objectIds, int[] previous)
        {
            if (objectIds == null) return previous ?? new int[0];
            objectIds.Sort();
            int write = 0;
            for (int read = 0; read < objectIds.Count; read++)
            {
                if (write > 0 && objectIds[read] == objectIds[write - 1])
                    continue;
                objectIds[write++] = objectIds[read];
            }
            if (write < objectIds.Count)
                objectIds.RemoveRange(write, objectIds.Count - write);
            if (previous != null && previous.Length == objectIds.Count)
            {
                bool unchanged = true;
                for (int index = 0; index < previous.Length; index++)
                {
                    if (previous[index] != objectIds[index])
                    {
                        unchanged = false;
                        break;
                    }
                }
                if (unchanged) return previous;
            }
            return objectIds.ToArray();
        }

        public static int[] Filter(int[] candidates, int[] sortedTargetableIds)
        {
            if (candidates == null || candidates.Length == 0 ||
                sortedTargetableIds == null)
                return candidates;
            List<int> accepted = null;
            for (int index = 0; index < candidates.Length; index++)
            {
                if (Array.BinarySearch(
                        sortedTargetableIds, candidates[index]) >= 0)
                {
                    if (accepted != null) accepted.Add(candidates[index]);
                }
                else if (accepted == null)
                {
                    accepted = new List<int>(candidates.Length - 1);
                    for (int prior = 0; prior < index; prior++)
                        accepted.Add(candidates[prior]);
                }
            }
            return accepted == null
                ? candidates
                : accepted.ToArray();
        }
    }
}
