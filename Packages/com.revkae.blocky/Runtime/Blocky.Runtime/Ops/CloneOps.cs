namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.create_clone</c> — copies an object, scripts and all, where it stands. The copy runs its own
    /// <c>when I start as a clone</c> script, which is what makes a clone useful: the original spawns, the copies
    /// do the work. An empty object input means "me", so the common case is an object cloning itself.
    /// The clone is announced after it exists, so its own scripts are subscribed before the hat fires.
    /// </summary>
    [BlockExecutor("control.create_clone")]
    public sealed class CreateCloneOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var original = BlockyRuntime.Objects.Find(ctx.GetText(0), ctx.Target);
            if (original == null) return OpResult.Continue;

            var clone = BlockyRuntime.Clones.Create(original);
            if (clone == null) return OpResult.Continue; // at the cap: quietly do nothing, as Scratch does

            // Subscribe the copy's own scripts before announcing it. Activating it normally does this through
            // OnEnable, but that is not guaranteed to have run yet outside play mode, and a clone that hears its
            // own hat a frame late is a clone that never starts. Initialize is idempotent, so this costs nothing
            // when OnEnable got there first.
            var runner = clone.GetComponent<ObjectProgramRunner>();
            if (runner != null) runner.Initialize();

            BlockyRuntime.Triggers.RaiseCloneStarted(clone);
            return OpResult.Continue;
        }
    }

    /// <summary>
    /// <c>control.delete_this_clone</c> — removes this copy. On the original it does nothing at all (deleting the
    /// object a learner is editing is never what was meant), so the thread simply ends either way: a cap block has
    /// nothing after it to run.
    /// </summary>
    [BlockExecutor("control.delete_this_clone")]
    public sealed class DeleteThisCloneOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Clones.Delete(ctx.Target);
            ctx.Thread.State = ThreadState.Done;
            return OpResult.Continue;
        }
    }
}
