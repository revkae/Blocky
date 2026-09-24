namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>sound.play_note</c> — Scratch's "play note for n beats": the note sounds and the script waits for it, so
    /// a row of these is a tune rather than a chord. The pitch is a MIDI note number, 60 being middle C.
    /// </summary>
    [BlockExecutor("sound.play_note")]
    public sealed class PlayNoteOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            if (ctx.Scratch == 0f)
            {
                var seconds = BlockyRuntime.Audio.PlayNote(ctx.Target, ctx.GetNumber(0), ctx.GetNumber(1));
                if (seconds <= 0f) return OpResult.Continue;

                ctx.Scratch = ctx.Now + seconds;
                ctx.Thread.State = ThreadState.Sleeping;
                ctx.Thread.WakeAt = ctx.Scratch;
                return OpResult.Retry;
            }

            ctx.Scratch = 0f;
            return OpResult.Continue;
        }
    }

    /// <summary>
    /// <c>sound.play</c> — Scratch's "start sound": plays a clip from <c>Resources/Sounds</c> and carries straight
    /// on, so sounds can overlap. A name nothing matches is silence rather than an error — there are no clips in
    /// the project yet, and a block that threw would make that a crash instead of a quiet afternoon.
    /// </summary>
    [BlockExecutor("sound.play")]
    public sealed class PlaySoundOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Audio.PlayClip(ctx.Target, ctx.GetText(0));
            return OpResult.Continue;
        }
    }

    /// <summary><c>sound.stop_all</c> — silence, everywhere.</summary>
    [BlockExecutor("sound.stop_all")]
    public sealed class StopAllSoundsOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Audio.StopAll();
            return OpResult.Continue;
        }
    }

    /// <summary><c>sound.set_volume</c> — 0–100, for this object's own sounds.</summary>
    [BlockExecutor("sound.set_volume")]
    public sealed class SetVolumeOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            BlockyRuntime.Audio.SetVolume(ctx.Target, ctx.GetNumber(0));
            return OpResult.Continue;
        }
    }
}
