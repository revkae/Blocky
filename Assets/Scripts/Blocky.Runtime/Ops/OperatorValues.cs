using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary><c>operator.add</c> — <c>(a) + (b)</c>. Inputs are read through the slot evaluator, so either side may hold another reporter.</summary>
    [BlockExecutor("operator.add")]
    public sealed class AddValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) => BlockValue.Number(ctx.GetNumber(0) + ctx.GetNumber(1));
    }

    /// <summary><c>operator.subtract</c> — <c>(a) - (b)</c>.</summary>
    [BlockExecutor("operator.subtract")]
    public sealed class SubtractValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) => BlockValue.Number(ctx.GetNumber(0) - ctx.GetNumber(1));
    }

    /// <summary><c>operator.multiply</c> — <c>(a) * (b)</c>.</summary>
    [BlockExecutor("operator.multiply")]
    public sealed class MultiplyValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) => BlockValue.Number(ctx.GetNumber(0) * ctx.GetNumber(1));
    }

    /// <summary>
    /// <c>operator.divide</c> — <c>(a) / (b)</c>. Dividing by zero answers 0 rather than infinity or NaN: the
    /// result usually feeds a transform, and one stray infinity there sends an object somewhere no learner can
    /// find it. (Scratch, with nothing to crash, reports Infinity.)
    /// </summary>
    [BlockExecutor("operator.divide")]
    public sealed class DivideValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            var divisor = ctx.GetNumber(1);
            return BlockValue.Number(divisor == 0f ? 0f : ctx.GetNumber(0) / divisor);
        }
    }

    /// <summary>
    /// <c>operator.mod</c> — the remainder, taking the sign of the divisor, as in Scratch: <c>-1 mod 4</c> is 3,
    /// not -1. That is what makes <c>mod</c> usable for wrapping a value into a range.
    /// </summary>
    [BlockExecutor("operator.mod")]
    public sealed class ModValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            var divisor = ctx.GetNumber(1);
            if (divisor == 0f) return BlockValue.Number(0f);
            var remainder = ctx.GetNumber(0) % divisor;
            if (remainder != 0f && (remainder < 0f) != (divisor < 0f)) remainder += divisor;
            return BlockValue.Number(remainder);
        }
    }

    /// <summary><c>operator.round</c> — to the nearest whole number.</summary>
    [BlockExecutor("operator.round")]
    public sealed class RoundValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) => BlockValue.Number(Mathf.Round(ctx.GetNumber(0)));
    }

    /// <summary>
    /// <c>operator.math</c> — Scratch's "[abs] of (n)" dropdown. The choice is read by index, so the order of the
    /// choices in the asset is part of the contract: abs, floor, ceiling, sqrt, sin, cos, tan, ln, log, e^, 10^.
    /// Angles are degrees, like every other angle a learner types in this editor.
    /// </summary>
    [BlockExecutor("operator.math")]
    public sealed class MathValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            var n = ctx.GetNumber(1);
            var result = ctx.Params[0].ChoiceIndex switch
            {
                1 => Mathf.Floor(n),
                2 => Mathf.Ceil(n),
                3 => n < 0f ? 0f : Mathf.Sqrt(n),
                4 => Mathf.Sin(n * Mathf.Deg2Rad),
                5 => Mathf.Cos(n * Mathf.Deg2Rad),
                6 => Mathf.Tan(n * Mathf.Deg2Rad),
                7 => n <= 0f ? 0f : Mathf.Log(n),
                8 => n <= 0f ? 0f : Mathf.Log10(n),
                9 => Mathf.Exp(n),
                10 => Mathf.Pow(10f, n),
                _ => Mathf.Abs(n)
            };
            return BlockValue.Number(result);
        }
    }

    /// <summary>
    /// <c>operator.random</c> — "pick random (from) to (to)". Whole numbers in, whole number out (both ends
    /// included, as in Scratch); give either end a decimal and the answer is a decimal.
    /// </summary>
    [BlockExecutor("operator.random")]
    public sealed class RandomValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            var from = ctx.GetNumber(0);
            var to = ctx.GetNumber(1);
            if (from > to) (from, to) = (to, from);

            var whole = Mathf.Approximately(from, Mathf.Round(from)) && Mathf.Approximately(to, Mathf.Round(to));
            return BlockValue.Number(whole
                ? Random.Range(Mathf.RoundToInt(from), Mathf.RoundToInt(to) + 1)
                : Random.Range(from, to));
        }
    }

    /// <summary><c>operator.join</c> — the two inputs written one after the other, as text.</summary>
    [BlockExecutor("operator.join")]
    public sealed class JoinValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) => BlockValue.Text(ctx.GetText(0) + ctx.GetText(1));
    }

    /// <summary><c>operator.letter_of</c> — letter (index) of (text), counting from 1. Out of range answers "".</summary>
    [BlockExecutor("operator.letter_of")]
    public sealed class LetterOfValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            var index = Mathf.RoundToInt(ctx.GetNumber(0));
            var text = ctx.GetText(1);
            return BlockValue.Text(index >= 1 && index <= text.Length ? text[index - 1].ToString() : string.Empty);
        }
    }

    /// <summary><c>operator.length</c> — how many letters are in the text.</summary>
    [BlockExecutor("operator.length")]
    public sealed class LengthValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx) => BlockValue.Number(ctx.GetText(0).Length);
    }
}
