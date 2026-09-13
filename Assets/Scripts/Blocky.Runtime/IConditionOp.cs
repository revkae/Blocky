using System;
using Blocky.Compiler;
using UnityEngine;

namespace Blocky.Runtime
{
    /// <summary>
    /// The runtime side of a condition block (<see cref="BlockShape.Boolean"/>): answers true or false, instantly
    /// and without side effects. Never scheduled as a step — whichever block owns the slot (<c>if</c>,
    /// <c>repeat until</c>) asks it through <see cref="OpContext.GetBool"/>, every time it needs the answer.
    /// Bound by <see cref="BlockExecutorAttribute"/> exactly like an <see cref="IBlockOp"/>. Stateless singleton.
    /// </summary>
    public interface IConditionOp
    {
        bool Evaluate(ref ConditionContext ctx);
    }

    /// <summary>What a condition can see: the object running the script and its own params (which may be conditions too).</summary>
    public ref struct ConditionContext
    {
        private readonly ConditionEvaluator _evaluator;

        public readonly VmThread Thread;
        public readonly ReadOnlySpan<ParamValue> Params;

        public GameObject Target => Thread.Target;

        public ConditionContext(ConditionEvaluator evaluator, VmThread thread, ReadOnlySpan<ParamValue> parameters)
        {
            _evaluator = evaluator;
            Thread = thread;
            Params = parameters;
        }

        /// <summary>Reads a Bool param, evaluating it if it's a condition slot — for conditions built from other conditions (not, and, or).</summary>
        public bool GetBool(int index) => _evaluator.Evaluate(Params[index], Thread);
    }

    /// <summary>Turns a param into true/false: a literal reads as itself, a condition slot runs its condition op, an empty slot is false.</summary>
    public sealed class ConditionEvaluator
    {
        private readonly IConditionOp[] _table; // indexed by opcode; null for every non-condition block

        public ConditionEvaluator(IConditionOp[] table)
        {
            _table = table ?? Array.Empty<IConditionOp>();
        }

        public bool Evaluate(in ParamValue value, VmThread thread)
        {
            if (value.Kind != Data.ParamKind.Reporter) return value.Boolean;

            var opcode = value.ReporterOpcode;
            if (opcode < 0 || opcode >= _table.Length || _table[opcode] == null) return false;

            var parameters = new ReadOnlySpan<ParamValue>(thread.Program.ParamTable, value.ReporterParamOffset, value.ReporterParamCount);
            var ctx = new ConditionContext(this, thread, parameters);
            return _table[opcode].Evaluate(ref ctx);
        }
    }
}
