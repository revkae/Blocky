using System.Collections.Generic;
using Blocky.Compiler;
using Blocky.Data;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    /// <summary>The learner's playback controls on <see cref="VmScheduler"/> (pause, step, speed) and <see cref="RunningBlocks"/>.</summary>
    public class VmPlaybackTests
    {
        private GameObject _target;
        private GameObject _other;

        [SetUp]
        public void SetUp()
        {
            _target = new GameObject("BlockyPlaybackTarget");
            _other = new GameObject("BlockyPlaybackOther");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_target);
            Object.DestroyImmediate(_other);
        }

        private static BlockDefinition Def(string blockType, BlockShape shape, ParamSpec[] parameters = null)
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.blockType = blockType;
            def.executorKey = blockType;
            def.shape = shape;
            def.parameters = parameters ?? new ParamSpec[0];
            return def;
        }

        private static BlockRegistry BuildRegistry() => BlockRegistry.Build(new[]
        {
            Def("event.when_play_clicked", BlockShape.Trigger),
            Def("motion.move_forward", BlockShape.Statement, new[]
            {
                new ParamSpec { key = "distance", kind = ParamKind.Number, min = -1000, max = 1000 },
                new ParamSpec { key = "duration", kind = ParamKind.Number, min = 0, max = 1000 }
            }),
            Def("control.wait", BlockShape.Statement, new[] { new ParamSpec { key = "seconds", kind = ParamKind.Number, min = 0, max = 1000 } })
        });

        private static BlockNode Move(string id, float distance, float duration) => new()
        {
            id = id,
            blockType = "motion.move_forward",
            parameters = new[]
            {
                new BlockParam { key = "distance", kind = ParamKind.Number, number = distance },
                new BlockParam { key = "duration", kind = ParamKind.Number, number = duration }
            }
        };

        private static BlockNode Wait(string id, float seconds) => new()
        {
            id = id,
            blockType = "control.wait",
            parameters = new[] { new BlockParam { key = "seconds", kind = ParamKind.Number, number = seconds } }
        };

        private static (VmScheduler scheduler, CompiledProgram compiled) Build(params BlockNode[] sequence)
        {
            var registry = BuildRegistry();
            var program = new ObjectProgram
            {
                stacks = new[] { new BlockStack { id = "stk_1", triggerBlockType = "event.when_play_clicked", sequence = sequence } }
            };
            var result = ProgramCompiler.Link(program, registry);
            Assert.IsFalse(result.HasErrors);
            return (new VmScheduler(OpTableBuilder.Build(registry, typeof(OpTableBuilder).Assembly)), result.Program);
        }

        private float Z => _target.transform.position.z;

        [Test]
        public void ActiveNodeId_IsTheBlockBeingRun_IncludingWhileAWaitSleeps()
        {
            var (scheduler, compiled) = Build(Wait("n_wait", 1f), Move("n_move", 10f, 2f));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(0.5f); // wait starts, wakes at 1.5
            Assert.AreEqual("n_wait", thread.ActiveNodeId);
            Assert.AreEqual(ThreadState.Sleeping, thread.State);

            scheduler.Tick(1.1f); // now 1.6: the wait ends and the timed move begins
            Assert.AreEqual("n_move", thread.ActiveNodeId);
        }

        [Test]
        public void Pause_FreezesScriptsAndTheirClock_ResumeCarriesOn()
        {
            var (scheduler, compiled) = Build(Move("n1", 10f, 2f));
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);
            Assert.AreEqual(5f, Z, 1e-3f);

            scheduler.Pause();
            scheduler.Tick(1f);
            Assert.AreEqual(5f, Z, 1e-3f);
            Assert.IsTrue(scheduler.IsPaused);

            scheduler.Resume();
            scheduler.Tick(1f);
            Assert.AreEqual(10f, Z, 1e-3f);
        }

        [Test]
        public void Pause_BeforeAScriptStarts_HoldsItUntilResume()
        {
            var (scheduler, compiled) = Build(Move("n1", 1f, 0f));
            scheduler.Pause();
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f);
            Assert.AreEqual(0f, Z, 1e-4f);
            Assert.IsTrue(scheduler.HasWork);

            scheduler.Resume();
            scheduler.Tick(1f);
            Assert.AreEqual(1f, Z, 1e-4f);
        }

        [Test]
        public void Step_RunsOneInstantBlockPerStep_ThenStaysPaused()
        {
            var (scheduler, compiled) = Build(Move("n1", 1f, 0f), Move("n2", 1f, 0f), Move("n3", 1f, 0f));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Step();
            scheduler.Tick(0.1f);
            Assert.AreEqual(1f, Z, 1e-4f);
            Assert.AreEqual("n1", thread.ActiveNodeId);
            Assert.IsFalse(scheduler.IsStepping);
            Assert.IsTrue(scheduler.IsPaused);

            scheduler.Tick(0.1f); // paused between steps: nothing happens
            Assert.AreEqual(1f, Z, 1e-4f);

            scheduler.Step();
            scheduler.Tick(0.1f);
            Assert.AreEqual(2f, Z, 1e-4f);
            Assert.AreEqual("n2", thread.ActiveNodeId);
        }

        [Test]
        public void Step_ATimedBlock_RunsItToTheEndOverSeveralTicks()
        {
            var (scheduler, compiled) = Build(Move("n1", 10f, 2f), Move("n2", 1f, 0f));
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Step();
            scheduler.Tick(1f);
            Assert.AreEqual(5f, Z, 1e-3f);
            Assert.IsTrue(scheduler.IsStepping);

            scheduler.Tick(1f);
            Assert.AreEqual(10f, Z, 1e-3f);
            Assert.IsFalse(scheduler.IsStepping);

            scheduler.Tick(1f); // n2 waits for the next step
            Assert.AreEqual(10f, Z, 1e-3f);
        }

        [Test]
        public void Step_InTheMiddleOfABlock_OnlyFinishesThatBlock()
        {
            var (scheduler, compiled) = Build(Move("n1", 10f, 2f), Move("n2", 1f, 0f));
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Tick(1f); // half-way through n1, running normally

            scheduler.Step();
            scheduler.Tick(1f);
            Assert.AreEqual(10f, Z, 1e-3f);
            Assert.IsFalse(scheduler.IsStepping);

            scheduler.Tick(1f);
            Assert.AreEqual(10f, Z, 1e-3f);
        }

        [Test]
        public void Step_TwoScripts_EachRunsOneBlock()
        {
            var (scheduler, compiled) = Build(Move("n1", 1f, 0f), Move("n2", 1f, 0f));
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Start(compiled, _other, compiled.StackEntryPoints[0]);

            scheduler.Step();
            scheduler.Tick(0.1f);

            Assert.AreEqual(1f, _target.transform.position.z, 1e-4f);
            Assert.AreEqual(1f, _other.transform.position.z, 1e-4f);
        }

        [Test]
        public void TimeScale_SlowsTimedBlocks()
        {
            var (scheduler, compiled) = Build(Move("n1", 10f, 2f));
            scheduler.TimeScale = 0.5f;
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(1f); // half a second of script time: a quarter of the move

            Assert.AreEqual(2.5f, Z, 1e-3f);
        }

        [Test]
        public void RestoreMoment_BringsScriptsBack_EvenOnesThatFinished()
        {
            var (scheduler, compiled) = Build(Move("n1", 1f, 0f), Move("n2", 1f, 0f));
            var thread = scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Step();
            scheduler.Tick(0.1f); // n1 ran; parked before n2
            var moment = scheduler.SaveMoment();

            scheduler.Step();
            scheduler.Tick(0.1f); // n2 ran and the script finished
            Assert.IsFalse(scheduler.HasWork);

            scheduler.RestoreMoment(moment);
            Assert.AreSame(thread, scheduler.Threads[0]); // the same thread object, so anything holding it stays valid
            Assert.AreEqual("n1", thread.ActiveNodeId);
            Assert.IsTrue(scheduler.IsPaused);

            scheduler.Step();
            scheduler.Tick(0.1f); // n2 runs again (the scene's objects aren't the scheduler's to put back)
            Assert.AreEqual(3f, Z, 1e-4f);
            Assert.AreEqual("n2", thread.ActiveNodeId);
        }

        [Test]
        public void RestoreMoment_AlsoRewindsATimedBlocksProgress()
        {
            var (scheduler, compiled) = Build(Move("n1", 10f, 2f));
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Tick(1f); // half-way: 1 s of 2 done
            var moment = scheduler.SaveMoment();
            scheduler.Tick(0.5f);

            scheduler.RestoreMoment(moment);
            scheduler.Resume();
            scheduler.Tick(1f); // the remaining 1 s: +5, not the +2.5 that was left after the extra tick

            Assert.AreEqual(12.5f, Z, 1e-3f);
            Assert.IsFalse(scheduler.HasWork);
        }

        [Test]
        public void FindLive_And_StopWhere_MatchTheObjectAndProgram()
        {
            var (scheduler, compiled) = Build(Move("n1", 10f, 2f));
            var entry = compiled.StackEntryPoints[0];
            var mine = scheduler.Start(compiled, _target, entry);
            scheduler.Start(compiled, _other, entry);

            Assert.AreSame(mine, scheduler.FindLive(_target, compiled, entry)); // found while still waiting to start

            scheduler.StopWhere(_target, compiled);
            Assert.IsNull(scheduler.FindLive(_target, compiled, entry));
            Assert.IsNotNull(scheduler.FindLive(_other, compiled, entry));
        }

        [Test]
        public void FinishedThreads_AreDroppedFromTheList()
        {
            var (scheduler, compiled) = Build(Move("n1", 1f, 0f));
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);

            scheduler.Tick(0.1f);

            Assert.AreEqual(0, scheduler.Threads.Count);
            Assert.IsFalse(scheduler.HasWork);
        }

        [Test]
        public void RunningBlocks_ListsEachRunningBlockOnceForThatObjectOnly()
        {
            var (scheduler, compiled) = Build(Move("n1", 10f, 2f));
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Start(compiled, _target, compiled.StackEntryPoints[0]);
            scheduler.Tick(1f);

            var ids = new List<string> { "stale" };
            RunningBlocks.Collect(scheduler, _target, ids);
            CollectionAssert.AreEqual(new[] { "n1" }, ids);

            RunningBlocks.Collect(scheduler, _other, ids);
            Assert.AreEqual(0, ids.Count);

            scheduler.Tick(1f); // both finish
            RunningBlocks.Collect(scheduler, _target, ids);
            Assert.AreEqual(0, ids.Count);
        }
    }
}
