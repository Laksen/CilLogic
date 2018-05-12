

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

        public ValueOperand(int value) { Value = value; }
        public ValueOperand(Opcode instruction) : this(instruction.Result) { }
    }

    public class UndefOperand : Operand
    {
        public override string ToString() { return "{Undef}"; }
    }

    internal class PhiOperand : Operand
    {
        public BasicBlock Block { get; }
        public Operand Value { get; }

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
        private MethodDefinition Method;

        public override string ToString() { return $"[{Method.Name}]"; }

        public MethodOperand(MethodDefinition method)
        {
            this.Method = method;
        }
    }
}