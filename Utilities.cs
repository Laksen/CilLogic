using System.Collections.Generic;
using System.Linq;
using CilLogic.CodeModel;
using Mono.Cecil;

namespace CilLogic.Utilities
{
    public static class CodeHelpers
    {
        public static List<Opcode> AllInstructions(this Method method)
        {
            return method.Blocks.SelectMany(x => x.Instructions).ToList();
        }

        public static List<BasicBlock> NextBlocks(this BasicBlock block)
        {
            return block.Instructions.Last().Operands.OfType<BlockOperand>().Select(x => x.Block).ToList();
        }
    }

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