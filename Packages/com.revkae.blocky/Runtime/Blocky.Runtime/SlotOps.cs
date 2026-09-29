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
        bool Evaluate(ref SlotContext ctx);
    }

    /// <summary>
    /// The runtime side of a reporter block (<see cref="BlockShape.Reporter"/>): answers a value — a number, a
    /// string, or either, depending on what it was given. Scratch's round blocks. Like a condition it never runs
    /// as a step; the block whose input holds it asks through <see cref="OpContext.GetNumber"/> /
    /// <see cref="OpContext.GetText"/>, on every read. Stateless singleton.
    /// </summary>
    public interface IValueOp
    {
        BlockValue Evaluate(ref SlotContext ctx);
    }

    /// <summary>
    /// What a block sitting in a slot can see: the object running the script, its own inputs (which may hold
    /// further slot blocks), and the script clock. Shared by conditions and reporters — the two differ in what
    /// they answer, not in what they can look at.
    /// </summary>
    public ref struct SlotContext
    {
        private readonly SlotEvaluator _evaluator;

        public readonly VmThread Thread;
        public readonly ReadOnlySpan<ParamValue> Params;

        public GameObject Target => Thread.Target;

        /// <summary>Scratch's timer, in seconds, on the script clock — 0 when evaluated outside a scheduler (a unit test).</summary>
        public float Timer => _evaluator.Scheduler?.Timer ?? 0f;

        /// <summary>The script clock, in seconds; 0 outside a scheduler.</summary>
        public float Now => _evaluator.Scheduler?.Now ?? 0f;

        public SlotContext(SlotEvaluator evaluator, VmThread thread, ReadOnlySpan<ParamValue> parameters)
        {
            _evaluator = evaluator;
            Thread = thread;
            Params = parameters;
        }

        /// <summary>Reads an input as true/false, running whatever block is in it — for conditions built from other conditions.</summary>
        public bool GetBool(int index) => _evaluator.Evaluate(Params[index], Thread);

        /// <summary>Reads an input as a number, running whatever block is in it — for <c>(a) + (b)</c> and friends.</summary>
        public float GetNumber(int index) => _evaluator.EvaluateValue(Params[index], Thread).AsNumber();

        /// <summary>Reads an input as text, running whatever block is in it.</summary>
        public string GetText(int index) => _evaluator.EvaluateValue(Params[index], Thread).AsText();

        /// <summary>The whole value, for a block that cares what it actually is — <c>=</c> compares numbers as numbers and words as words.</summary>
        public BlockValue GetValue(int index) => _evaluator.EvaluateValue(Params[index], Thread);
    }

    /// <summary>
    /// Turns an input into an answer: a literal reads as itself, a slot holding a block runs that block's op, an
    /// empty slot is false / 0 / "". One evaluator serves both tables, so a reporter can sit inside a condition
    /// (<c>(x) &gt; (3)</c>) and a condition inside a reporter (<c>join &lt;touching?&gt; …</c>) without either
    /// table knowing about the other.
    /// </summary>
    public sealed class SlotEvaluator
    {
        private readonly IConditionOp[] _conditions; // indexed by opcode; null for every non-condition block
        private readonly IValueOp[] _values;         // indexed by opcode; null for every non-reporter block

        /// <summary>The scheduler these slots are being evaluated for, or null in a unit test that made an evaluator by hand.</summary>
        public VmScheduler Scheduler { get; }

        public SlotEvaluator(IConditionOp[] conditions, IValueOp[] values = null, VmScheduler scheduler = null)
        {
            _conditions = conditions ?? Array.Empty<IConditionOp>();
            _values = values ?? Array.Empty<IValueOp>();
            Scheduler = scheduler;
        }

        /// <summary>True/false from an input: a <c>Bool</c> literal reads as itself, a filled slot runs its block and coerces.</summary>
        public bool Evaluate(in ParamValue value, VmThread thread)
        {
            if (value.Kind != Data.ParamKind.Reporter) return value.Boolean;

            var opcode = value.ReporterOpcode;
            if (opcode < 0) return false;

            if (opcode < _conditions.Length && _conditions[opcode] != null)
            {
                var ctx = Context(value, thread);
                return _conditions[opcode].Evaluate(ref ctx);
            }

            return EvaluateValue(value, thread).AsBool(); // a reporter dropped into a boolean input
        }

        /// <summary>The value of an input: a literal reads as itself, a filled slot runs its block, an empty slot is "".</summary>
        public BlockValue EvaluateValue(in ParamValue value, VmThread thread)
        {
            switch (value.Kind)
            {
                case Data.ParamKind.Number:
                    return BlockValue.Number(value.Number);
                case Data.ParamKind.Bool:
                    return BlockValue.Boolean(value.Boolean);
                case Data.ParamKind.Reporter:
                    break;
                default: // Text, Choice, ObjectRef
                    return BlockValue.Text(value.Text ?? string.Empty);
            }

            var opcode = value.ReporterOpcode;
            if (opcode < 0) return BlockValue.Empty;

            if (opcode < _values.Length && _values[opcode] != null)
            {
                var ctx = Context(value, thread);
                return _values[opcode].Evaluate(ref ctx);
            }

            if (opcode < _conditions.Length && _conditions[opcode] != null)
            {
                var ctx = Context(value, thread); // a condition dropped into a value input
                return BlockValue.Boolean(_conditions[opcode].Evaluate(ref ctx));
            }

            return BlockValue.Empty;
        }

        private SlotContext Context(in ParamValue value, VmThread thread)
        {
            var parameters = new ReadOnlySpan<ParamValue>(thread.Program.ParamTable, value.ReporterParamOffset, value.ReporterParamCount);
            return new SlotContext(this, thread, parameters);
        }
    }
}
