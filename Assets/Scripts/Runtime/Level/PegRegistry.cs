using System.Collections.Generic;
using TwistedTangle.Runtime.Data.ScriptableObjects;
using UnityEngine;

namespace TwistedTangle.Runtime.Level
{
    /// <summary>
    /// Runtime counterpart of the editor's peg discovery: loads every <see cref="PegDefinitionSO"/>
    /// from Resources and indexes it by <see cref="PegDefinitionSO.TypeId"/>. Because both sides
    /// discover peg types from data, a new peg asset works in game with zero code changes.
    /// </summary>
    public class PegRegistry
    {
        private const string ResourcesSubPath = "Data/Pegs";
        private readonly Dictionary<string, PegDefinitionSO> _byTypeId = new();

        public PegRegistry()
        {
            // Load from the dedicated folder first; fall back to a project-wide scan so pegs placed
            // in any Resources folder still resolve.
            var defs = Resources.LoadAll<PegDefinitionSO>(ResourcesSubPath);
            if (defs == null || defs.Length == 0)
                defs = Resources.LoadAll<PegDefinitionSO>(string.Empty);

            foreach (var def in defs)
                if (def != null)
                    _byTypeId[def.TypeId] = def;
        }

        public bool TryGet(string typeId, out PegDefinitionSO def) => _byTypeId.TryGetValue(typeId, out def);

        public PegDefinitionSO Get(string typeId) =>
            _byTypeId.TryGetValue(typeId, out var def) ? def : null;
    }
}
