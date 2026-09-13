using Blocky.Data;

namespace Blocky.Compiler
{
    /// <summary>
    /// A resolved, flat param slot in <see cref="CompiledProgram.ParamTable"/> — no key string at runtime.
    /// A <see cref="ParamKind.Reporter"/> slot holds no value of its own: it names the condition op to evaluate
    /// (<see cref="ReporterOpcode"/>, -1 for an empty slot, which reads as false) and where that condition's own
    /// params sit in the same table.
    /// </summary>
    public readonly struct ParamValue
    {
        public readonly ParamKind Kind;
        public readonly float Number;
        public readonly string Text;
        public readonly bool Boolean;
        public readonly int ChoiceIndex; // index into the ParamSpec's choices array, -1 if not a Choice

        public readonly int ReporterOpcode;      // Reporter: opcode of the condition block in the slot, -1 if empty
        public readonly int ReporterParamOffset; // Reporter: that block's first param in the ParamTable
        public readonly int ReporterParamCount;

        public ParamValue(ParamKind kind, float number, string text, bool boolean, int choiceIndex)
        {
            Kind = kind;
            Number = number;
            Text = text;
            Boolean = boolean;
            ChoiceIndex = choiceIndex;
            ReporterOpcode = -1;
            ReporterParamOffset = 0;
            ReporterParamCount = 0;
        }

        private ParamValue(int reporterOpcode, int reporterParamOffset, int reporterParamCount)
        {
            Kind = ParamKind.Reporter;
            Number = 0f;
            Text = null;
            Boolean = false;
            ChoiceIndex = -1;
            ReporterOpcode = reporterOpcode;
            ReporterParamOffset = reporterParamOffset;
            ReporterParamCount = reporterParamCount;
        }

        /// <summary>A condition slot holding the block <paramref name="opcode"/>, whose params start at <paramref name="paramOffset"/>.</summary>
        public static ParamValue Reporter(int opcode, int paramOffset, int paramCount) => new(opcode, paramOffset, paramCount);

        /// <summary>An empty condition slot — evaluates to false, like Scratch's empty hexagon.</summary>
        public static ParamValue EmptyReporter => new(-1, 0, 0);
    }
}
