using System;

namespace Blocky.Runtime
{
    /// <summary>Binds an <see cref="IBlockOp"/> to the <c>executorKey</c> named on its <c>BlockDefinition</c>.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BlockExecutorAttribute : Attribute
    {
        public string ExecutorKey { get; }

        public BlockExecutorAttribute(string executorKey)
        {
            ExecutorKey = executorKey;
        }
    }
}
