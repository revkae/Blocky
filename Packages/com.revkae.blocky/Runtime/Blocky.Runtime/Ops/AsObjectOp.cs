namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>control.as_object</c> — "as [object]": the blocks in its mouth act on that object instead of the one
    /// running the script, and after it the script's own object is back (ADR-042). The mouth is a frame that carries
    /// the object (<see cref="Frame.As"/>); every block reads what it acts on through <see cref="OpContext.Target"/>,
    /// which finds the innermost such frame — so these nest, and a custom block run from inside acts on that object too.
    /// A name no object answers to skips the mouth, with one warning per run of the script, and the script carries on.
    /// The object is recorded for Reset and Step back the first time it is used, like a programmed one.
    /// </summary>
    [BlockExecutor("control.as_object")]
    public sealed class AsObjectOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var thread = ctx.Thread;

            if (thread.FrameCount > 0 && thread.TopFrame().OwnerPc == ctx.Pc)
            {
                thread.PopFrame(); // the mouth just finished: the blocks after it act on what they did before
                ctx.NextPc = ctx.Instruction.JumpAExit;
                return OpResult.Jump;
            }

            var name = ctx.GetText(0);
            var other = BlockyRuntime.Objects.Find(name, ctx.Target); // "me" inside another "as" is that block's object
            if (other == null)
            {
                thread.WarnAsObjectMissing(name);
                ctx.NextPc = ctx.Instruction.JumpAExit;
                return OpResult.Jump;
            }

            BlockyRuntime.World.CaptureDriven(thread.Target, other);
            thread.PushFrame(Frame.As(ctx.Pc, ctx.Instruction.JumpAExit, other));
            ctx.NextPc = ctx.Instruction.JumpA;
            return OpResult.Jump;
        }
    }
}
