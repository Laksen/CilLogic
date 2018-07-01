

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using CilLogic.Utilities;

namespace CilLogic.CodeModel
{
    public class SelfOperand : Operand
    {
        public SelfOperand(TypeDef operandType) : base(operandType)
        {
        }

        public override string ToString() { return "[SELF]"; }
    }

    internal class FieldOperand : Operand
    {
        public FieldReference Field { get; }

        public override string ToString() { return $"[{Field.Name}][{OperandType.GetWidth()}]"; }

        public FieldOperand(FieldReference field, Method method) : base(new CecilType<TypeDefinition>(field.FieldType.Resolve(method.MethodRef, method.GenericParams), method))
        {
            this.Field = field;
        }

        public override int GetHashCode()
        {
            return Field.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return
                (obj is FieldOperand fo) &&
                EqualityComparer<FieldReference>.Default.Equals(Field, fo.Field);
        }
    }

    public class ValueOperand : Operand
    {
        public int Value { get; }

        public override string ToString() { return $"%{Value}[{OperandType.GetWidth()}]"; }

        public override bool Equals(object obj)
        {
            if (obj is ValueOperand po)
                return (Value == po.Value);
            else
                return false;
        }

        public override int GetHashCode() { return Value; }

        public ValueOperand(int value, TypeDef type) : base(type) { Value = value; }
        public ValueOperand(Opcode instruction) : this(instruction.Result, instruction.GetResultType(instruction.Block.Method)) { }
    }

    public class CondValue : Operand
    {
        public Operand Condition;
        public Operand Value;

        public override bool Equals(object obj)
        {
            return
                (obj is CondValue cv) &&
                EqualityComparer<Operand>.Default.Equals(Condition, cv.Condition) &&
                EqualityComparer<Operand>.Default.Equals(Value, cv.Value);
        }

        public override int GetHashCode() { return Value.GetHashCode() ^ Condition.GetHashCode(); }

        public override string ToString()
        {
            return $"{Condition}?{Value}";
        }

        public CondValue(Operand condition, Operand value, TypeDef type) : base(type)
        {
            Condition = condition;
            Value = value;
        }
    }

    public class UndefOperand : Operand
    {

        public override bool Equals(object obj)
        {
            return (obj is UndefOperand);
        }

        public override int GetHashCode() { return 0; }

        public override string ToString() { return "{Undef}"; }

        public UndefOperand() : base(null) { }
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

        public PhiOperand(BasicBlock block, Operand value) : base(value.OperandType)
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

        public Int64 SignedValue
        {
            get
            {
                return ((Int64)Value << (64 - Width))
>> (64 - Width);
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is ConstOperand po)
                return
                    ((Value == 0) && (po.Value == 0)) || 
                    ((Value == po.Value) && (Signed == po.Signed) && (Width == po.Width));
            else
                return false;
        }

        public override int GetHashCode() { return (Value == 0) ? 0 : (int)(Value + (UInt64)(Signed ? 1000 : 0 + Width)); }

        public override string ToString() { return Value.ToString(); }

        public ConstOperand(UInt64 value) : base(VectorType.Int64) { Value = value; Signed = true; Width = 64; }
        public ConstOperand(int value) : base(VectorType.Int32) { Value = (UInt64)(UInt32)value; Signed = true; Width = 32; }

        public ConstOperand(ulong value, bool signed, int width) : base(new VectorType(width, signed))
        {
            Value = value;
            Signed = signed;
            Width = width;
        }

        public ConstOperand(UInt64 value, TypeDefinition typeDefinition, Method method) : base(new CecilType<TypeDefinition>(typeDefinition.Resolve(method.MethodRef, method.GenericParams), method))
        {
            Value = value; Signed = false; Width = typeDefinition.GetWidth(method);
        }
    }

    public class InstrOperand : Operand
    {
        public Opcode Instruction { get; }

        public override string ToString() { return "@BB" + Instruction.Block.Id; }

        public InstrOperand(Opcode instruction) : base(TypeDef.Void) { Instruction = instruction; }
    }

    public class BlockOperand : Operand
    {
        public BasicBlock Block { get; }

        public override string ToString() { return "BB" + Block.Id; }

        public BlockOperand(BasicBlock block) : base(TypeDef.Void) { Block = block; }
    }

    internal class MethodOperand : Operand
    {
        public MethodReference Method { get; }

        public override string ToString() { return $"[{Method.Name}]"; }

        public MethodOperand(MethodReference methodRef, Method method) : base(new CecilType<MethodReference>(methodRef, method))
        {
            this.Method = methodRef;
        }
    }

    public class LocOperand : Operand
    {
        public int Location { get; }

        public LocOperand(int location, TypeReference type, Method method) : base(new CecilType<TypeDefinition>(type.Resolve(method.MethodRef, method.GenericParams), method))
        {
            Location = location;
        }
    }

    public class TypeOperand : Operand
    {
        public TypeOperand(TypeReference type, Method method) : base(new CecilType<TypeDefinition>(type.Resolve(method.MethodRef, method.GenericParams), method))
        {
        }
    }

    public class ArgumentOperand : Operand
    {
        public int Index { get; }
        public ArgumentOperand(int index, TypeDef operandType) : base(operandType)
        {
            Index = index;
        }
    }
}