using System;
using System.Collections.Generic;
using System.Linq;
using CilLogic.CodeModel;
using CilLogic.Types;
using Mono.Cecil;

namespace CilLogic.Utilities
{
    public static class CodeHelpers
    {
        public static bool IsPot(this UInt64 value, out int bits)
        {
            bits = 0;
            if (((value - 1) & value) == 0)
            {
                bits = 0;

                for (int i = 0; i < 64; i++)
                    if ((((UInt64)1) << i) == value)
                        bits = i;
                        
                return true;
            }
            else
                return false;
        }

        private static bool IsPortType(this TypeDefinition td)
        {
            return td.Name == "IPort";
        }

        private static Dictionary<TypeReference, bool> PortDict = new Dictionary<TypeReference, bool>();

        private static bool IsPort(this TypeReference oper)
        {
            if (!PortDict.ContainsKey(oper))
            {
                var res = false;

                var td = oper.Resolve();
                if (td.FullName == typeof(IPipe<>).FullName)
                    res = true;

                if (td.Interfaces.Any(i => IsPort(i.InterfaceType)))
                    res = true;

                PortDict[oper] = res;
            }

            return PortDict[oper];
        }

        public static bool IsStateInvariant(this BasicBlock block)
        {
            return block.Instructions.All(i => i.Operands.OfType<FieldOperand>().All(o => o.Field.FieldType.IsPort()));
        }

        private class Vertex
        {
            public BasicBlock Block;
            public int Index;
            public int LowLink;
            internal bool OnStack;
        }

        public static List<List<BasicBlock>> FindConnectedComponents(this Method method)
        {
            List<List<BasicBlock>> res = new List<List<BasicBlock>>();

            const int Undefined = -1;

            var V = method.Blocks.ToDictionary(b => b, b => new Vertex { Block = b, Index = Undefined, LowLink = Undefined, OnStack = false });

            var s = new Stack<Vertex>();
            var index = 0;

            foreach (var v in V.Values)
            {
                if (v.Index == Undefined)
                    StrongConnect(v);
            }

            void StrongConnect(Vertex v)
            {
                v.Index = index;
                v.LowLink = index;
                index++;

                s.Push(v);
                v.OnStack = true;

                foreach (var w in v.Block.NextBlocks().Select(x => V[x]))
                {
                    if (w.Index == Undefined)
                    {
                        StrongConnect(w);
                        v.LowLink = Math.Min(v.LowLink, w.LowLink);
                    }
                    else if (w.OnStack)
                    {
                        v.LowLink = Math.Min(v.LowLink, w.Index);
                    }
                }

                if (v.LowLink == v.Index)
                {
                    var r = new List<BasicBlock>();

                    Vertex w;
                    do
                    {
                        w = s.Pop();
                        w.OnStack = false;
                        r.Add(w.Block);
                    }
                    while (w != v);

                    res.Add(r);
                }
            }

            return res;
        }

        public static List<Opcode> AllInstructions(this Method method)
        {
            return method.Blocks.SelectMany(x => x.Instructions).ToList();
        }

        public static List<BasicBlock> NextBlocks(this BasicBlock block)
        {
            return block.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(x => x.Block).ToList();
        }
    }

    public static class AssemblyHelpers
    {
        public static int GetArgCount(this MethodDefinition m)
        {
            return (m.HasThis ? 1 : 0) + m.Parameters.Count;
        }

        private static Dictionary<AssemblyDefinition, ILookup<string, TypeDefinition>> asmTypes = new Dictionary<AssemblyDefinition, ILookup<string, TypeDefinition>>();

        public static TypeDefinition FindType(this AssemblyDefinition asm, string fullName)
        {
            if (!asmTypes.ContainsKey(asm))
                asmTypes[asm] = asm.Modules.SelectMany(m => m.Types).ToLookup(t => t.FullName);
            return asmTypes[asm][fullName].FirstOrDefault();
        }
    }
}