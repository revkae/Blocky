using System;

namespace Blocky.Data
{
    public sealed class SetParam : IProgramCommand
    {
        private readonly ParamTarget _target;
        private readonly string _key;
        private readonly BlockParam _newValue;
        private BlockParam _oldValue;

        public SetParam(ParamTarget target, string key, BlockParam newValue)
        {
            _target = target;
            _key = key;
            _newValue = newValue;
        }

        public void Do(ProgramStore store)
        {
            var parameters = ResolveParameters(store.Program);
            var index = FindIndex(parameters);
            _oldValue = parameters[index];
            parameters[index] = _newValue;
            store.RaiseChanged(new StructureChange(_target.StackId, _target.NodeId, StructureChangeKind.ParamChanged));
        }

        public void Undo(ProgramStore store)
        {
            var parameters = ResolveParameters(store.Program);
            parameters[FindIndex(parameters)] = _oldValue;
            store.RaiseChanged(new StructureChange(_target.StackId, _target.NodeId, StructureChangeKind.ParamChanged));
        }

        private int FindIndex(BlockParam[] parameters)
        {
            var index = Array.FindIndex(parameters, p => p.key == _key);
            if (index < 0) throw new InvalidOperationException($"Param '{_key}' not found.");
            return index;
        }

        private BlockParam[] ResolveParameters(ObjectProgram program)
        {
            var stack = ProgramQuery.FindStack(program, _target.StackId)
                ?? throw new InvalidOperationException($"Stack '{_target.StackId}' not found.");

            if (_target.NodeId == null) return stack.triggerParameters;

            var node = ProgramQuery.FindNode(program, _target.StackId, _target.NodeId)
                ?? throw new InvalidOperationException($"Node '{_target.NodeId}' not found.");
            return node.parameters;
        }

        public string Describe() => $"Set {_key}";
    }
}
