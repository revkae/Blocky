using System.Collections.Generic;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// Which blocks are running right now on one object — what the editor lights up so a learner can connect
    /// "this block" with "that movement". Reads the scheduler's live threads and fills a list the caller owns,
    /// so polling it every frame allocates nothing.
    /// </summary>
    public static class RunningBlocks
    {
        /// <summary>Clears <paramref name="nodeIds"/>, then adds the block each live thread on <paramref name="target"/> is on (each id once).</summary>
        public static void Collect(VmScheduler scheduler, GameObject target, List<string> nodeIds)
        {
            nodeIds.Clear();
            if (target == null) return;

            var threads = scheduler.Threads;
            for (var i = 0; i < threads.Count; i++)
            {
                var thread = threads[i];
                if (thread.State == ThreadState.Done || thread.Target != target) continue;

                var nodeId = thread.ActiveNodeId;
                if (nodeId != null && !nodeIds.Contains(nodeId)) nodeIds.Add(nodeId);
            }
        }
    }
}
