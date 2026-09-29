using System;
using Blocky.Runtime.Triggers;
using UnityEngine;

// Blocky resets these statics itself at the start of every Play session (ADR-014), so the statics-cleanup analyzer has nothing to add.
#pragma warning disable UAL0010, UAL0013

namespace Blocky.Runtime
{
    /// <summary>How a script ended by itself, as <see cref="BlockyEvents.ScriptFinished"/> reports it.</summary>
    public enum ScriptEndReason
    {
        /// <summary>It ran out of blocks.</summary>
        Completed,

        /// <summary>A <c>stop</c> block ended it: <c>this script</c>, <c>all</c>, or <c>other scripts</c> run by another script.</summary>
        StoppedByBlock,

        /// <summary>One of its blocks failed; the console has a warning naming the block.</summary>
        Failed
    }

    /// <summary>One script — a hat and the blocks under it — running on one object, as <see cref="BlockyEvents"/> reports it.</summary>
    public readonly struct BlockyScript
    {
        /// <summary>The object running the script.</summary>
        public readonly GameObject Object;

        /// <summary>Which of the object's scripts it is: its place in the program's list of stacks, or -1 when that can't be told.</summary>
        public readonly int Index;

        /// <summary>The block type of its hat — which event started it, such as <c>event.when_go_clicked</c>.</summary>
        public readonly string Trigger;

        public BlockyScript(GameObject obj, int index, string trigger)
        {
            Object = obj;
            Index = index;
            Trigger = trigger;
        }

        internal static BlockyScript Of(VmThread thread)
        {
            var index = thread.Program.StackIndexFor(thread.EntryPc);
            var types = thread.Program.StackTriggerTypes;
            return new BlockyScript(thread.Target, index, index >= 0 && types != null && index < types.Length ? types[index] : null);
        }

        public override string ToString() => $"{(Object != null ? Object.name : "(destroyed)")} #{Index} ({Trigger})";
    }

    /// <summary>
    /// Blocky's events and commands for game code — the hooks a puzzle game, a tutorial or a scoring system needs:
    /// when the learner's scripts start and finish, when the level is complete, what the scripts broadcast, and when
    /// the learner presses Stop or Reset or edits a program. Messages go the other way too: <see cref="Broadcast"/>
    /// starts every <c>when I receive</c> script, and <see cref="CompleteLevel"/> does what the <c>level complete</c>
    /// block does. The run bar's commands (Go, Stop, Reset, Step, speed) are on <see cref="BlockyRuntime.Playback"/>.
    /// <para>
    /// Everything is raised on the main thread, while Blocky runs its scripts (from <see cref="BlockyRuntimeTicker"/>)
    /// or from the call that caused it. A handler that throws is logged and never stops the scripts. Handlers are
    /// dropped at the start of every Play session — domain reload is off in this project, so a handler left from
    /// the last session would belong to an object that no longer exists: subscribe in <c>OnEnable</c> or <c>Start</c>,
    /// and unsubscribe in <c>OnDisable</c>.
    /// </para>
    /// </summary>
    public static class BlockyEvents
    {
        /// <summary>A script began running — the frame after its event happened.</summary>
        public static event Action<BlockyScript> ScriptStarted;

        /// <summary>
        /// A script ended by itself: it ran out of blocks, a <c>stop</c> block ended it, or a block failed. Not raised
        /// when something outside ends it — the Stop or Reset button (see <see cref="Stopped"/>), an edit to its
        /// program, its object being disabled or destroyed, or its own event restarting it.
        /// </summary>
        public static event Action<BlockyScript, ScriptEndReason> ScriptFinished;

        /// <summary>
        /// The last running script finished and nothing is waiting to start — "the program is done", which is when a
        /// puzzle checks the result. Raised again each time scripts run and finish; not raised by the Stop button.
        /// </summary>
        public static event Action AllScriptsFinished;

        /// <summary>
        /// The level is complete: a <c>level complete</c> block ran (the argument is its object), or game code called
        /// <see cref="CompleteLevel"/>. Every <c>when level complete</c> script starts as well. Raised each time — a
        /// goal touched twice completes twice, so ignore repeats if they matter.
        /// </summary>
        public static event Action<GameObject> LevelCompleted;

        /// <summary>A broadcast went out — from a <c>broadcast</c> block or from <see cref="Broadcast"/> — with its message.</summary>
        public static event Action<string> MessageSent;

        /// <summary>The run bar's Stop (or Reset) ended every script. Game state that isn't a programmed object can reset here.</summary>
        public static event Action Stopped;

        /// <summary>The run bar's Reset put every programmed object back where it started, after <see cref="Stopped"/>.</summary>
        public static event Action WorldReset;

        /// <summary>The learner changed an object's program in the in-game editor (the argument is the object).</summary>
        public static event Action<GameObject> ProgramEdited;

        /// <summary>Sends <paramref name="message"/> as if a <c>broadcast</c> block had: every <c>when I receive</c> script for it starts.</summary>
        public static void Broadcast(string message) => BlockyRuntime.Triggers.Broadcast(message);

        /// <summary>Completes the level as the <c>level complete</c> block does: <see cref="LevelCompleted"/>, and every <c>when level complete</c> script.</summary>
        public static void CompleteLevel(GameObject by = null) => BlockyRuntime.Triggers.RaiseLevelCompleted(by);

        /// <summary>For an editor that changes programs: reports the change as <see cref="ProgramEdited"/>. Blocky's in-game editor calls it.</summary>
        public static void ReportProgramEdited(GameObject obj) => Raise(ProgramEdited, obj);

        // ---- wiring ------------------------------------------------------------------------------------

        /// <summary>Forwards <paramref name="scheduler"/>'s thread reports. <see cref="BlockyRuntime"/> calls it once for each scheduler it uses.</summary>
        internal static VmScheduler Watch(VmScheduler scheduler)
        {
            if (scheduler == null) return null;
            scheduler.ThreadStarted += thread => Raise(ScriptStarted, BlockyScript.Of(thread));
            scheduler.ThreadFinished += thread => Raise(ScriptFinished, BlockyScript.Of(thread), thread.EndReason ?? ScriptEndReason.Completed);
            scheduler.AllThreadsFinished += () => Raise(AllScriptsFinished);
            return scheduler;
        }

        /// <summary>Forwards <paramref name="broker"/>'s broadcasts and level completions. Called once for each broker in use.</summary>
        internal static TriggerBroker Watch(TriggerBroker broker)
        {
            if (broker == null) return null;
            broker.OnBroadcast += message => Raise(MessageSent, message);
            broker.OnLevelCompleted += by => Raise(LevelCompleted, by);
            return broker;
        }

        internal static void RaiseStopped() => Raise(Stopped);

        internal static void RaiseWorldReset() => Raise(WorldReset);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession()
        {
            ScriptStarted = null;
            ScriptFinished = null;
            AllScriptsFinished = null;
            LevelCompleted = null;
            MessageSent = null;
            Stopped = null;
            WorldReset = null;
            ProgramEdited = null;
        }

        // Each handler on its own: one that throws is logged, and the rest still hear the event.

        private static void Raise(Action handlers)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((Action)handler)(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        private static void Raise<T>(Action<T> handlers, T arg)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((Action<T>)handler)(arg); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        private static void Raise<T1, T2>(Action<T1, T2> handlers, T1 arg1, T2 arg2)
        {
            if (handlers == null) return;
            foreach (var handler in handlers.GetInvocationList())
            {
                try { ((Action<T1, T2>)handler)(arg1, arg2); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }
}
