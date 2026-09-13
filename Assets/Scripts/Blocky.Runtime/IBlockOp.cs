namespace Blocky.Runtime
{
    /// <summary>The runtime implementation of a block's behaviour. Stateless singleton; all mutable state lives on the thread.</summary>
    public interface IBlockOp
    {
        OpResult Execute(ref OpContext ctx);
    }
}
