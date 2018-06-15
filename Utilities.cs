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
        public static bool IsBool(this TypeDef type)
        {
            return (type.GetWidth() == 1);
        }

        public static bool IsPot(this UInt64 value, out int bits)
        {
            bits = 0;
            if (value == 0) return false;

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

        public static bool IsSlice(this UInt64 value, out int msb, out int lsb)
        {
            msb = 0;
            lsb = 0;
            if (value == 0) return false;

            for (int i = 0; i < 64; i++)
                if (IsPot((value >> i) + 1, out int bits))
                {
                    lsb = i;
                    msb = i + bits - 1;
                    return true;
                }

            return false;
        }

        private static bool IsPortType(this TypeDefinition td)
        {
            return td.Name == "IPort";
        }

        private static Dictionary<TypeReference, bool> PortDict = new Dictionary<TypeReference, bool>();

        internal static bool IsPort(this TypeReference oper)
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

    public struct FieldInfo
    {
        public int Msb, Lsb;
    }

    public static class AssemblyHelpers
    {
        public static TypeDefinition Resolve(this TypeReference type, MemberReference scope, Dictionary<GenericParameter, TypeDefinition> outerArgs)
        {
            var res = type.Resolve();
            if (res != null) return res;

            if (type.IsGenericParameter && (type is GenericParameter gp))
            {
                if (outerArgs != null && outerArgs.ContainsKey(gp))
                    return outerArgs[gp];

                if (scope != null && (scope is IGenericInstance git) && ((scope as MethodReference)?.GetElementMethod() is IGenericParameterProvider gpp) && gpp.GenericParameters.Contains(gp))
                    return git.GenericArguments[gp.Position].Resolve(outerArgs);

                if (scope != null && (scope is IGenericInstance git2) && ((scope as TypeReference)?.GetElementType() is IGenericParameterProvider gpp2) && gpp2.GenericParameters.Contains(gp))
                    return git2.GenericArguments[gp.Position].Resolve(outerArgs);

                if (scope != null && (scope.DeclaringType != null))
                    return Resolve(type, scope.DeclaringType, outerArgs);
            }

            if (type is TypeDefinition td)
                return td;

            return null;
        }

        public static TypeDefinition Resolve(this TypeReference type, Dictionary<GenericParameter, TypeDefinition> scope)
        {
            var res = type.Resolve();
            if (res != null) return res;

            if (type.IsGenericParameter && (type is GenericParameter gp) && scope.ContainsKey(gp))
                return scope[gp];

            if (type is TypeDefinition td)
                return td;

            return null;
        }

        public static TypeDefinition Resolve(this TypeReference type, MethodReference scope)
        {
            var res = type.Resolve();
            if (res != null) return res;

            if (type.ContainsGenericParameter && scope.IsGenericInstance)
            {
                var pos = (type as GenericParameter).Position;
                return (scope as IGenericInstance).GenericArguments[pos].Resolve(scope);
            }

            if (type is TypeDefinition td)
                return td;

            return null;
        }

        public static int GetWidth(this TypeReference type, Method method, MemberReference scope = null)
        {
            var r = type.Resolve();

            if (r.CustomAttributes.Any(t => t.AttributeType.Resolve().FullName == typeof(BitWidthAttribute).FullName))
            {
                var w = r.CustomAttributes.First(t => t.AttributeType.Resolve().FullName == typeof(BitWidthAttribute).FullName);
                return (Int32)w.ConstructorArguments[0].Value;
            }

            if (r == r.Module.TypeSystem.Boolean) return 1;

            if (r == r.Module.TypeSystem.SByte) return 8;
            if (r == r.Module.TypeSystem.Int16) return 16;
            if (r == r.Module.TypeSystem.Int32) return 32;
            if (r == r.Module.TypeSystem.Int64) return 64;

            if (r == r.Module.TypeSystem.Byte) return 8;
            if (r == r.Module.TypeSystem.UInt16) return 16;
            if (r == r.Module.TypeSystem.UInt32) return 32;
            if (r == r.Module.TypeSystem.UInt64) return 64;

            if (r.IsEnum)
            {
                return 64;
            }
            else if (r.IsValueType && r.IsClass && !r.IsPrimitive)
                return r.Fields.Sum(f => f.GetWidth(method));

            if (type.IsPort())
            {
                return (type.GenericParameters.Last().Resolve(method?.MethodRef, method?.GenericParams) ?? type.GenericParameters.Last().Resolve(scope, method?.GenericParams))?.GetWidth(method) ?? 0;
            }

            if (r.IsClass && !r.IsValueType)
                return 0;

            throw new NotSupportedException("Type not detected");
        }

        public static int GetWidth(this FieldReference field, Method method)
        {
            var r = field.Resolve();

            if (r.CustomAttributes.Any(t => t.AttributeType.Resolve().FullName == typeof(BitWidthAttribute).FullName))
            {
                var w = r.CustomAttributes.First(t => t.AttributeType.Resolve().FullName == typeof(BitWidthAttribute).FullName);
                return (Int32)w.ConstructorArguments[0].Value;
            }

            return field.FieldType.GetWidth(method);
        }

        public static FieldInfo GetInfo(this FieldDefinition field, TypeDefinition scope, Method method)
        {
            var lsb = 0;
            var width = field.GetWidth(method);

            if (field.DeclaringType == scope)
            {
                for (int i = 0; i < scope.Fields.Count; i++)
                {
                    if (scope.Fields[i] == field)
                        return new FieldInfo { Lsb = lsb, Msb = lsb + width - 1 };
                    else
                        lsb += scope.Fields[i].FieldType.GetWidth(method);
                }
            }
            else
            {
                throw new NotSupportedException("structs inside structs");
            }

            throw new InvalidOperationException("Could not find field");
        }

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