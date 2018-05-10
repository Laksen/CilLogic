

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

    public class ConstOperand : Operand
    {
        public UInt64 Value { get; }

        public override string ToString() { return $"{Value}"; }

        public ConstOperand(UInt64 value) { Value = value; }
        public ConstOperand(int value) : this((UInt64)value) { }
    }

    public class InstrOperand : Operand
    {
        public Opcode Instruction { get; }

        public override string ToString() { return "INST"; } // TODO

        public InstrOperand(Opcode instruction) { Instruction = instruction; }
    }

    internal class MethodOperand : Operand
    {
        private MethodDefinition Method;

        public override string ToString() { return $"[{Method.Name}]"; } // TODO

        public MethodOperand(MethodDefinition method)
        {
            this.Method = method;
        }
    }
}