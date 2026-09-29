using System.Collections.Generic;
using Blocky.Runtime.Triggers;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// The run bar's commands, for every script in the scene at once: Go, Stop, Reset, Pause/Resume, Step forward,
    /// Step back, and speed. Step back works from snapshots: before each step forward it records every script's
    /// progress (<see cref="VmScheduler.SaveMoment"/>) and every programmed object (<see cref="WorldSnapshot.SaveMoment"/>),
    /// and Step back puts the most recent one back. Replaying from the start instead would need physics and input
    /// to repeat exactly, which they don't.
    /// The history only covers steps taken since the scripts were paused: running normally, stopping, resetting or
    /// editing a program forgets it, because those snapshots no longer describe what's on screen.
    /// </summary>
    public sealed class Playback
    {
        public const int MaxSpeed = 4;
        public const int MaxStepsBack = 200;

        private sealed class Moment
        {
            public SchedulerMoment Scripts;
            public WorldMoment World;
            public bool PlayClickedArmed; // "when Play clicked" hadn't fired yet — stepping back past the start must re-arm it
            public GameObject Target;     // the one object this step was taken for, or null for a whole-scene step
        }

        private readonly VmScheduler _scheduler;
        private readonly TriggerBroker _triggers;
        private readonly WorldSnapshot _world;
        private readonly BlockyClones _clones;
        private readonly BlockyAudio _audio;
        private readonly List<Moment> _history = new();
        private readonly HashSet<GameObject> _going = new(); // objects Go was pressed for
        private bool _goPressed;

        public Playback(VmScheduler scheduler, TriggerBroker triggers, WorldSnapshot world, BlockyClones clones = null,
            BlockyAudio audio = null)
        {
            _scheduler = scheduler;
            _triggers = triggers;
            _world = world;
            _clones = clones;
            _audio = audio;
        }

        /// <summary>1×–<see cref="MaxSpeed"/>×: how fast timed blocks and waits run.</summary>
        public int Speed { get; private set; } = 1;

        /// <summary>Some script is running or waiting to run — so there's something to pause.</summary>
        public bool IsRunning => _scheduler.HasWork;

        public bool IsPaused => _scheduler.IsPaused;

        /// <summary>A step forward is still finishing (a timed block or a wait takes several frames).</summary>
        public bool IsStepping => _scheduler.IsStepping;

        public bool CanStepBack => _history.Count > 0;

        /// <summary>Whether <paramref name="only"/> has a step to go back to (null: a whole-scene step).</summary>
        public bool CanStepBackFor(GameObject only) => FindMoment(only) >= 0;

        /// <summary>Whether <paramref name="only"/>'s scripts are frozen — paused on its own or with the scene (null: the whole scene).</summary>
        public bool IsPausedFor(GameObject only) => only == null ? IsPaused : _scheduler.IsPausedFor(only);

        /// <summary>
        /// <see cref="IsGoing"/> for one object: Go was pressed for it and its scripts are still running. With null,
        /// the scene-wide <see cref="IsGoing"/>.
        /// </summary>
        public bool IsGoingFor(GameObject only)
        {
            if (only == null) return IsGoing;
            if (_going.Contains(only) && !_scheduler.HasWorkOn(only)) _going.Remove(only);
            return _going.Contains(only);
        }

        private int FindMoment(GameObject only)
        {
            for (var i = _history.Count - 1; i >= 0; i--)
                if (_history[i].Target == only) return i;
            return -1;
        }

        private void ForgetStepsOf(GameObject only) => _history.RemoveAll(m => m.Target == only);

        /// <summary>
        /// Go was pressed and the scripts are still running — what the Pause button waits for. Scripts that start on
        /// their own (at scene start, on a key) don't count. Clears itself once everything has finished, so a script
        /// a key starts later doesn't bring Pause back.
        /// </summary>
        public bool IsGoing
        {
            get
            {
                if (_goPressed && !_scheduler.HasWork) _goPressed = false;
                return _goPressed;
            }
        }

        /// <summary>
        /// Runs every "when Go clicked" script, un-pausing first. With nothing running (never started, stopped, reset,
        /// finished, or stopped by an edit) it also re-runs the "when Play clicked" scripts — so Go always starts
        /// everything, and Reset then Go replays the scene from the beginning.
        /// </summary>
        public void Go(GameObject only = null)
        {
            Resume(only);
            if (only == null)
            {
                if (!_scheduler.HasWork) _triggers.ResetPlaySession(); // a finished scene starts over, not just its Go scripts
                _triggers.FirePlayClicked(); // does nothing while the scene's first run is still going
                _triggers.FireGoClicked();
                _goPressed = true;
            }
            else
            {
                // Just this object: its "when Play clicked" scripts start again only if it has nothing running.
                if (!_scheduler.HasWorkOn(only)) _triggers.FirePlayClickedFor(only);
                _triggers.FireGoClicked(only);
                _going.Add(only);
            }
        }

        /// <summary>
        /// Ends every running script where it is, and re-arms "when Play clicked" so Go (or Step forward) runs every
        /// script again from there. Without that, most scripts — which start on "when Play clicked", once per session —
        /// would silently not restart after Stop. Objects stay where they got to: <see cref="ResetWorld"/> puts them back.
        /// With <paramref name="only"/>, just that object's scripts end (and its speech bubble and sound); the rest of
        /// the scene runs on, and the scene-wide "Stopped" event is not raised, because the scene hasn't stopped.
        /// </summary>
        public void Stop(GameObject only = null)
        {
            if (only != null)
            {
                ForgetStepsOf(only);
                _going.Remove(only);
                _scheduler.StopObject(only);
                BlockySpeechBubble.Hide(only);
                _audio?.StopFor(only);
                return;
            }

            _history.Clear();
            _goPressed = false;
            _going.Clear();
            _scheduler.StopAll();
            _scheduler.Resume();
            _triggers.ResetPlaySession();
            _clones?.DeleteAll(); // a run never leaves its copies behind, exactly as Scratch clears clones on stop
            BlockySpeechBubble.HideAll(); // nor a bubble mid-sentence
            _audio?.StopAll();            // nor a note still sounding
            BlockyEvents.RaiseStopped();  // and game code resets whatever it keeps itself
        }

        /// <summary>
        /// Stops every script and puts every programmed object back how it started, ready for <see cref="Go"/>.
        /// With <paramref name="only"/>, just that object is stopped and put back (no scene-wide "WorldReset" event).
        /// </summary>
        public void ResetWorld(GameObject only = null)
        {
            Stop(only);
            if (only != null)
            {
                _world.RestoreObject(only);
                return;
            }

            _world.RestoreAll();
            BlockyEvents.RaiseWorldReset();
        }

        /// <summary>Freezes every script where it is — or, with <paramref name="only"/>, that object's scripts while the rest carry on.</summary>
        public void Pause(GameObject only = null) => _scheduler.Pause(only);

        /// <summary>
        /// Runs normally again (the scene, or with <paramref name="only"/> just that object). The step history is
        /// forgotten: once time runs on, the saved steps no longer lead to what's on screen.
        /// </summary>
        public void Resume(GameObject only = null)
        {
            if (only == null) _history.Clear();
            else ForgetStepsOf(only);
            _scheduler.Resume(only);
        }

        /// <summary>
        /// Remembers this moment, then pauses and runs one block of every script — starting, first, every script
        /// that isn't running yet, whatever hat it has (<see cref="TriggerBroker.FireStepAll"/>). That last part is
        /// the whole point: between steps the scene is frozen, so a learner can't press the key, cause the collision
        /// or look at the object that a "when key pressed" / "when collided" / "when looked at" script waits for.
        /// Stepping starts them by hand instead, so every script on the table can be walked a block at a time.
        /// A script already running is left exactly where it is and simply advances one block; a script that has
        /// finished starts again from its first block on the next press.
        /// Does nothing when there is no script to start at all, and while the previous step is still finishing
        /// (so every remembered moment is a clean stop between blocks).
        /// With <paramref name="only"/> (the object open in the editor), just that object's scripts step: its hats
        /// are started by hand, that object alone is paused between steps, and every other object carries on running.
        /// </summary>
        public void StepForward(GameObject only = null)
        {
            if (_scheduler.IsStepping) return;

            var moment = new Moment
            {
                Scripts = _scheduler.SaveMoment(only),
                World = _world.SaveMoment(only),
                PlayClickedArmed = only == null && !_triggers.PlayClickedFired,
                Target = only
            };

            // Scripts that ran to their end leave "when Play clicked" spent; without re-arming it here, Step after a
            // finished run started nothing and silently froze the scene.
            var wasRunning = only == null ? _scheduler.HasWork : _scheduler.HasWorkOn(only);
            if (!wasRunning)
            {
                if (only == null)
                {
                    _triggers.ResetPlaySession();
                    _goPressed = false; // this run was started by Step, not Go
                }
                else _going.Remove(only);
            }
            // Firing "when Play clicked" would start every other object's scripts too; the step below starts this one's.
            if (only == null) _triggers.FirePlayClicked(); // at most once per session: keeps the flag honest for Go and for Step back
            _triggers.FireStepAll(only);                   // every other hat that isn't already running

            var hasWork = only == null ? _scheduler.HasWork : _scheduler.HasWorkOn(only);
            if (!hasWork) return; // nothing to step through: don't pause the scene for nothing

            _history.Add(moment);
            if (_history.Count > MaxStepsBack) _history.RemoveAt(0);
            _scheduler.Step(only);
        }

        /// <summary>
        /// Goes back to just before the last step forward: scripts, objects and all. Stays paused. With
        /// <paramref name="only"/>, undoes that object's last step and touches nothing else.
        /// </summary>
        public void StepBack(GameObject only = null)
        {
            var index = FindMoment(only);
            if (index < 0) return;

            var moment = _history[index];
            _history.RemoveAt(index);

            _scheduler.RestoreMoment(moment.Scripts);
            _world.RestoreMoment(moment.World);
            if (moment.PlayClickedArmed) _triggers.ResetPlaySession();
        }

        /// <summary>Sets the speed, clamped to 1–<see cref="MaxSpeed"/>.</summary>
        public void SetSpeed(int speed)
        {
            Speed = speed < 1 ? 1 : speed > MaxSpeed ? MaxSpeed : speed;
            _scheduler.TimeScale = Speed;
        }

        /// <summary>Call when a program changes: the saved steps hold scripts of the old version, which must not come back.</summary>
        public void ForgetSteps(GameObject only = null)
        {
            if (only == null) _history.Clear();
            else ForgetStepsOf(only);
        }
    }
}
