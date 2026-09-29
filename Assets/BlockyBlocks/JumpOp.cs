using Blocky.Runtime;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>
/// "jump" — made with Blocky › New Block. The block itself is Resources/Blocks/game_jump.asset,
/// and its words are in Resources/Languages/*.blocks.json, next to it.
/// </summary>
[BlockExecutor("game.jump")]
[Preserve] // Blocky makes this class by reflection and nothing else refers to it: keep it in stripped builds
public sealed class JumpOp : IBlockOp
{
    [Preserve]
    public JumpOp() { }

    public OpResult Execute(ref OpContext ctx)
    {
        var self = ctx.Target; // the object running the script
        var amount = ctx.GetNumber(0); // "amount"

        // What the block does. Return OpResult.Continue when it's done. For something that takes time, keep
        // its progress in ctx.Scratch and return OpResult.Retry until it's finished — see Blocky's MoveForwardOp.
        return OpResult.Continue;
    }
}
