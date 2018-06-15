

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using CilLogic.Utilities;

namespace CilLogic.CodeModel
{
    public class Operand
    {
        public TypeDef OperandType { get; set; }
        public readonly string Stack;

        public Operand(TypeDef operandType)
        {
            //Stack = new System.Diagnostics.StackTrace(true).ToString();
            OperandType = operandType;
        }

        public static implicit operator Operand(UInt64 value) { return new ConstOperand(value); }
        public static implicit operator Operand(int value) { return new ConstOperand(value); }

        public static implicit operator Operand(Opcode instruction) { return new InstrOperand(instruction); }
    }

    public enum Op
    {
        Nop,

        Mov,
        Add, Sub,
        And, Or, Xor,
        Lsl, Lsr, Asr,
        Ceq, Clt, Cltu,
        Slice,

        InitObj,
        CreateStruct,
        LdFld, StFld,
        LdArray, StArray,
        LdLoc, StLoc,
        LdArg,

        BrFalse, BrTrue, Br, BrCond,
        Call, Return, Switch,
        Conv,
        Phi,
        LdLocA,
        Request, ReadReady, WritePort,
        ReadPort, ReadValid,
        Stall,
        Mux, InSet, NInSet,
        Insert,

        Reg
    }

    public class Opcode
    {
        public int Result { get; }
        public Op Op { get; }
        public List<Operand> Operands { get; set; }
        public BasicBlock Block { get; set; }
        public int Schedule { get; set; }

        public static Opcode Nop { get { return new Opcode(0, Op.Nop); } }

        public Opcode Next { get { return Block.GetNext(this); } }
        public Opcode Previous { get { return Block.GetPrev(this); } }

        public TypeDef GetResultType(Method method)
        {
            switch (Op)
            {
                case Op.Call:
                    return new CecilType<TypeDefinition>((this[0] as MethodOperand).Method.MethodReturnType.ReturnType.Resolve((this[0] as MethodOperand).Method, Block.Method.GenericParams), method);
                case Op.LdFld:
                    return new CecilType<TypeDefinition>((this[1] as FieldOperand).Field.FieldType.Resolve((this[1] as FieldOperand).Field.FieldType, Block.Method.GenericParams), method, (this[1] as FieldOperand).Field.FieldType);
                case Op.LdLoc:
                    return (this[1] as TypeOperand).OperandType;

                case Op.Reg:
                case Op.Mov:
                case Op.Insert:
                case Op.ReadPort: return this[0].OperandType;

                case Op.Request:
                case Op.LdArray:
                    {
                        if (this[0] is FieldOperand fo)
                            return new CecilType<TypeDefinition>(fo.Field.FieldType.GetElementType().Resolve(fo.Field.FieldType, Block.Method.GenericParams), method);
                        else if (this[0] is ValueOperand vo1)
                            return vo1.OperandType;
                        else
                            return TypeDef.Unknown;
                    }

                case Op.ReadValid:
                case Op.ReadReady:

                case Op.NInSet:
                case Op.InSet:
                case Op.Ceq:
                case Op.Clt:
                case Op.Cltu: return VectorType.UInt1;

                case Op.Conv: return new VectorType((int)(this[2] as ConstOperand).Value, (this[1] as ConstOperand).Value != 0);

                case Op.Slice:
                    {
                        var msb = (int)(this[1] as ConstOperand).Value;
                        var lsb = (int)(this[2] as ConstOperand).Value;
                        var shift = (int)(this[3] as ConstOperand).Value;
                        var signed = (int)(this[4] as ConstOperand).Value;

                        return new VectorType(msb - lsb + 1 + shift, signed != 0);
                    }

                case Op.Phi: return this[0].OperandType;
                case Op.Mux: return this[1].OperandType;

                case Op.Asr:
                case Op.Lsr:
                case Op.Lsl:
                case Op.And:
                case Op.Or:
                case Op.Xor:
                case Op.Add:
                case Op.Sub: return this[0].OperandType;

                case Op.StArray:
                case Op.StFld: return TypeDef.Void;

                //default: throw new NotSupportedException($"Invalid Op during type inferrence {Op}.");
                default: return TypeDef.Unknown;
            }
        }

        public bool IsCondJump
        {
            get
            {
                switch (Op)
                {
                    case Op.Switch:
                    case Op.BrCond:
                    case Op.BrFalse:
                    case Op.BrTrue:
                        return true;

                    default:
                        return false;
                }
            }
        }

        public Operand this[int index]
        {
            get
            {
                return Operands[index];
            }
        }

        public bool HasSideEffects()
        {
            switch (Op)
            {
                case Op.StArray:
                case Op.StFld:
                case Op.StLoc:

                case Op.InitObj:

                case Op.Switch:
                case Op.Br:
                case Op.BrFalse:
                case Op.BrTrue:
                case Op.BrCond:

                case Op.Call:
                case Op.Return:

                case Op.Request:
                case Op.ReadPort:
                case Op.WritePort:
                case Op.Stall:
                    return true;

                default:
                    return false;
            }
        }

        public override string ToString()
        {
            return "  " + (Result != 0 ? $"%{Result}[{GetResultType(Block.Method).GetWidth()}] = " : "") + Op + " " + string.Join(", ", Operands);
            //return "  " + (Result != 0 ? $"%{Result} = " : "") + Op + " " + string.Join(", ", Operands);
        }

        internal bool IsTerminating()
        {
            switch (Op)
            {
                case Op.Return:
                case Op.Br:
                case Op.BrCond:
                    return true;
                default:
                    return false;
            }
        }

        public Opcode(int result, Op op) : this(result, op, new Operand[0])
        {
        }

        public Opcode(int result, Op op, params Operand[] operands)
        {
            Result = result;
            Op = op;
            Operands = operands.ToList();
        }
    }

    public class BasicBlock
    {
        private static int counter = 1;

        public int Id { get; }
        public List<Opcode> Instructions { get; }
        public Method Method { get; set; }

        public Opcode Append(Opcode instruction)
        {
            Instructions.Add(instruction);
            instruction.Block = this;
            return instruction;
        }

        public Opcode Prepend(Opcode instruction)
        {
            Instructions.Insert(0, instruction);
            instruction.Block = this;
            return instruction;
        }

        public void InsertAfter(Opcode instruction, Opcode after)
        {
            var idx = Instructions.IndexOf(after);
            if (idx == Instructions.Count - 1)
                Instructions.Add(instruction);
            else
                Instructions.Insert(idx + 1, instruction);
            instruction.Block = this;
        }

        internal void InsertBefore(Opcode instruction, Opcode before)
        {
            var idx = Instructions.IndexOf(before);
            Instructions.Insert(idx, instruction);
            instruction.Block = this;
        }

        public BasicBlock SplitBefore(Opcode instruction)
        {
            var idx = Instructions.IndexOf(instruction);
            if (idx < 0)
                return this;
            if (idx == 0)
                return this;
            else
            {
                var newBlock = Method.GetBlock();
                while (Instructions.Count > idx)
                {
                    var instr = Instructions[idx];
                    Instructions.RemoveAt(idx);
                    newBlock.Append(instr);
                }

                Method.ReplacePhiSource(this, newBlock);

                Append(new Opcode(0, Op.Br, new BlockOperand(newBlock)));

                return newBlock;
            }
        }

        public override string ToString()
        {
            var isEntry = Method.Entry == this ? ">" : "";
            return $"{isEntry}BB{Id}:\n" + string.Join(Environment.NewLine, Instructions);
        }

        internal Opcode GetPrev(Opcode opcode)
        {
            var idx = Instructions.IndexOf(opcode);
            if (idx == 0)
                return null;
            else
                return Instructions[idx - 1];
        }

        internal Opcode GetNext(Opcode opcode)
        {
            var idx = Instructions.IndexOf(opcode);
            if (idx == Instructions.Count - 1)
                return null;
            else
                return Instructions[idx + 1];
        }

        internal void Replace(Opcode oldOpcode, Opcode newOpcode)
        {
            InsertAfter(newOpcode, oldOpcode);
            Instructions.Remove(oldOpcode);
            oldOpcode.Block = null;
        }

        public BasicBlock(Method method)
        {
            Id = counter++;
            Instructions = new List<Opcode>();
            Method = method;
        }
    }

    public class Method
    {
        public MethodReference MethodRef { get; }
        public Dictionary<GenericParameter, TypeDefinition> GenericParams { get; }
        public List<BasicBlock> Blocks { get; }
        public BasicBlock Entry { get; set; }
        public int Locals { get; }
        public TypeDefinition[] LocalTypes { get; }
        public bool IsSSA { get; internal set; }

        private static int ValueCounter = 2;

        public int GetValue()
        {
            return ValueCounter++;
        }

        public override string ToString()
        {
            return string.Join(Environment.NewLine, Blocks);
        }

        public BasicBlock GetBlock()
        {
            var blk = new BasicBlock(this);
            Blocks.Add(blk);
            return blk;
        }

        public void Fragment()
        {
            /*var jumpTargets = Blocks.SelectMany(b => b.Instructions.SelectMany(i => i.Operands)).OfType<InstrOperand>().Select(i => i.Instruction).ToList();
            foreach (var target in jumpTargets)
                target.Block.SplitBefore(target);

            var jumps = Blocks.SelectMany(b => b.Instructions.Where(i => i.IsCondJump)).ToList();
            foreach (var target in jumps)
                target.Block.SplitBefore(target.Next);*/

            foreach (var instr in Blocks.SelectMany(b => b.Instructions.Where(i => i.Operands.OfType<InstrOperand>().Any())))
            {
                foreach (var oper in instr.Operands.OfType<InstrOperand>().ToList())
                    instr.Operands[instr.Operands.IndexOf(oper)] = new BlockOperand(oper.Instruction.Block);
            }
        }

        public List<Opcode> ReplaceValue(int value, Operand newOperand)
        {
            var res = new HashSet<Opcode>();

            bool TryReplace(ref Operand oper)
            {
                if ((oper is ValueOperand vo) && vo.Value == value)
                {
                    oper = newOperand;
                    return true;
                }
                return false;
            }

            foreach (var instr in Blocks.SelectMany(b => b.Instructions))
            {
                for (int i = 0; i < instr.Operands.Count; i++)
                {
                    var oper = instr[i];
                    if (TryReplace(ref oper))
                    {
                        instr.Operands[i] = oper;
                        res.Add(instr);
                    }
                    else if ((instr[i] is PhiOperand po) && (po.Value is ValueOperand vo3) && (vo3.Value == value))
                    {
                        instr.Operands[i] = new PhiOperand(po.Block, newOperand);
                        res.Add(instr);
                    }
                    else if (instr[i] is CondValue cv)
                    {
                        var x = TryReplace(ref cv.Condition);
                        var y = TryReplace(ref cv.Value);

                        if (x || y)
                            res.Add(instr);
                    }
                }
            }

            return res.ToList();
        }

        internal void ReplaceBlockOperand(BasicBlock target, BlockOperand newTarget)
        {
            foreach (var instr in Blocks.SelectMany(b => b.Instructions).ToList())
            {
                for (int i = 0; i < instr.Operands.Count; i++)
                    if ((instr.Operands[i] is BlockOperand bo) && bo.Block == target)
                        instr.Operands[i] = newTarget;
                    else if ((instr.Operands[i] is PhiOperand po) && po.Block == target)
                        instr.Operands[i] = new PhiOperand(newTarget.Block, po.Value);
            }
        }

        internal void ReplacePhiSource(BasicBlock target, BasicBlock newTarget)
        {
            foreach (var instr in Blocks.SelectMany(b => b.Instructions).ToList())
            {
                for (int i = 0; i < instr.Operands.Count; i++)
                    if ((instr.Operands[i] is PhiOperand po) && po.Block == target)
                        instr.Operands[i] = new PhiOperand(newTarget, po.Value);
            }
        }

        public Method(MethodDefinition methodDef, MethodReference methodRef, Dictionary<GenericParameter, TypeDefinition> generics)
        {
            MethodRef = methodRef;
            GenericParams = generics;

            Blocks = new List<BasicBlock>();
            Entry = GetBlock();
            Locals = methodDef.Body.Variables.Count;
            LocalTypes = methodDef.Body.Variables.Select(x => x.VariableType.Resolve(generics)).ToArray();
        }
    }
}