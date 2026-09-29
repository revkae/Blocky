using Blocky.Data;
using Blocky.Data.Serialization;
using UnityEngine;

namespace Blocky.Runtime.Persistence
{
    /// <summary>Canvas persistence (TDD §13 Milestone 6): a program saved as JSON via <see cref="ProgramSerializer"/>, wrapped in an asset so it can be assigned in the Inspector.</summary>
    [CreateAssetMenu(menuName = "Blocky/Program", fileName = "NewBlockProgram")]
    public sealed class BlockProgramAsset : ScriptableObject
    {
        [SerializeField, TextArea(10, 40)]
        private string json = "";

        public ObjectProgram Load() => string.IsNullOrEmpty(json) ? new ObjectProgram() : ProgramSerializer.Deserialize(json);

        public void Save(ObjectProgram program) => json = ProgramSerializer.Serialize(program);
    }
}
