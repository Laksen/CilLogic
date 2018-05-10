using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace CilLogic.Utilities
{
    public static class AssemblyHelpers
    {
        private static Dictionary<AssemblyDefinition, ILookup<string, TypeDefinition>> asmTypes = new Dictionary<AssemblyDefinition, ILookup<string, TypeDefinition>>();

        public static TypeDefinition FindType(this AssemblyDefinition asm, string fullName)
        {
            if (!asmTypes.ContainsKey(asm))
                asmTypes[asm] = asm.Modules.SelectMany(m => m.Types).ToLookup(t => t.FullName);
            return asmTypes[asm][fullName].FirstOrDefault();
        }
    }
}