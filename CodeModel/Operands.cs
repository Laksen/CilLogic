

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace CilLogic.CodeModel
{
    public class SelfOperand : Operand
    {
        public override string ToString() { return "[SELF]"; }
    }

    internal class FieldOperand : Operand
    {
        public FieldReference Field { get; }

        public override string ToString() { return $"[{Field.Name}]"; }

        public FieldOperand(FieldReference field)
        {
            this.Field = field;
        }
    }

    public class ValueOperand : Operand
    {
        public int Value { get; }

        public override string ToString() { return $"%{Value}"; }

        public override bool Equals(object obj)
        {
            if (obj is ValueOperand po)
                return (Value == po.Value);
            else
                return false;
        }

        public override int GetHashCode() { return Value; }

        public ValueOperand(int value) { Value = value; }
        public ValueOperand(Opcode instruction) : this(instruction.Result) { }
    }

    public class UndefOperand : Operand
    {

        public override bool Equals(object obj)
        {
            return (obj is UndefOperand);
        }

        public override int GetHashCode() { return 0; }

        public override string ToString() { return "{Undef}"; }
    }

    internal class PhiOperand : Operand
    {
        public BasicBlock Block { get; }
        public Operand Value { get; }

        public override bool Equals(object obj)
        {
            if (obj is PhiOperand po)
                return (Block == po.Block) && Value.Equals(po.Value);
            else
                return false;
        }

        public override int GetHashCode() { return Block.Id + Value.GetHashCode(); }

        public override string ToString() { return $"BB{Block.Id}@{Value}"; }

        public PhiOperand(BasicBlock block, Operand value)
        {
            Block = block;
            Value = value;
        }
    }

    public class ConstOperand : Operand
    {
        public UInt64 Value { get; }
        public bool Signed { get; }
        public int Width { get; }

        public Int64 SignedValue { get { return (Int64)Value; } }

        public override bool Equals(object obj)
        {
            if (obj is ConstOperand po)
                return (Value == po.Value) && (Signed == po.Signed) && (Width == po.Width);
            else
                return false;
        }

        public override int GetHashCode() { return (int)(Value + (UInt64)(Signed ? 1000 : 0 + Width)); }

        public override string ToString() { return $"{Value}"; }

        public ConstOperand(UInt64 value) { Value = value; Signed = true; Width = 64; }
        public ConstOperand(int value) { Value = (UInt64)value; Signed = true; Width = 32; }

        public ConstOperand(ulong value, bool signed, int width) : this(value)
        {
            Signed = signed;
            Width = width;
        }
    }

    public class InstrOperand : Operand
    {
        public Opcode Instruction { get; }

        public override string ToString() { return "@BB" + Instruction.Block.Id; }

        public InstrOperand(Opcode instruction) { Instruction = instruction; }
    }

    public class BlockOperand : Operand
    {
        public BasicBlock Block { get; }

        public override string ToString() { return "BB" + Block.Id; }

        public BlockOperand(BasicBlock block) { Block = block; }
    }

    internal class MethodOperand : Operand
    {
        public MethodDefinition Method { get; }

        public override string ToString() { return $"[{Method.Name}]"; }

        public MethodOperand(MethodDefinition method)
        {
            this.Method = method;
        }
    }

    public class LocOperand : Operand
    {
        public int Location { get; }

        public LocOperand(int location)
        {
            Location = location;
        }
    }

    public class TypeOperand : Operand
    {
        public TypeDefinition TypeDef { get; }

        public TypeOperand(TypeDefinition typeDef)
        {
            TypeDef = typeDef;
        }
    }
}