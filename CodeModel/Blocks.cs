

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

namespace CilLogic.CodeModel
{
    public class Operand
    {
        public static implicit operator Operand(UInt64 value) { return new ConstOperand(value); }
        public static implicit operator Operand(int value) { return new ConstOperand(value); }

        public static implicit operator Operand(Opcode instruction) { return new InstrOperand(instruction); }
    }

    public enum Op
    {
        Nop,

        Add, Sub,
        And, Or, Xor,
        Lsl, Lsr, Asr,
        Ceq, Clt, Cltu,

        LdFld, StFld,
        LdArray, StArray,
        LdLoc, StLoc,
        LdArg,

        BrFalse, BrTrue, Br, BrCond,
        Call, Return, Switch,
        Conv,
    }

    public class Opcode
    {
        public int Result { get; }
        public Op Op { get; }
        public List<Operand> Operands { get; }
        public BasicBlock Block { get; set; }

        public static Opcode Nop { get { return new Opcode(0, Op.Nop); } }

        public Opcode Next { get { return Block.GetNext(this); } }
        public Opcode Previous { get { return Block.GetPrev(this); } }

        public bool IsCondJump
        {
            get
            {
                switch (Op)
                {
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

                case Op.Switch:
                case Op.Br:
                case Op.BrFalse:
                case Op.BrTrue:
                case Op.BrCond:

                case Op.Call:
                case Op.Return:
                    return true;
                default:
                    return false;
            }
        }

        public override string ToString()
        {
            return "  " + (Result != 0 ? "%" + Result + " = " : "") + Op + " " + string.Join(", ", Operands);
        }

        internal bool IsTerminating()
        {
            switch (Op)
            {
                case Op.Switch:
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
        public Method Method { get; }

        public Opcode Append(Opcode instruction)
        {
            Instructions.Add(instruction);
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

                Append(new Opcode(0, Op.Br, instruction));

                return newBlock;
            }
        }

        public override string ToString()
        {
            return $"BB{Id}:\n" + string.Join(Environment.NewLine, Instructions);
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

        public BasicBlock(Method method)
        {
            Id = counter++;
            Instructions = new List<Opcode>();
            Method = method;
        }
    }

    public class Method
    {
        public List<BasicBlock> Blocks { get; }
        public BasicBlock Entry { get; }

        private static int ValueCounter = 1;

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
            var jumpTargets = Blocks.SelectMany(b => b.Instructions.SelectMany(i => i.Operands)).OfType<InstrOperand>().Select(i => i.Instruction).ToList();
            foreach (var target in jumpTargets)
                target.Block.SplitBefore(target);

            var jumps = Blocks.SelectMany(b => b.Instructions.Where(i => i.IsCondJump)).ToList();
            foreach (var target in jumps)
                target.Block.SplitBefore(target.Next);

            foreach (var instr in Blocks.SelectMany(b => b.Instructions.Where(i => i.Operands.OfType<InstrOperand>().Any())))
            {
                foreach (var oper in instr.Operands.OfType<InstrOperand>().ToList())
                    instr.Operands[instr.Operands.IndexOf(oper)] = new BlockOperand(oper.Instruction.Block);
            }
        }

        public IEnumerable<Opcode> ReplaceValue(int value, ConstOperand constOperand)
        {
            foreach (var instr in Blocks.SelectMany(b => b.Instructions).ToList())
            {
                bool ok = false;

                for (int i = 0; i < instr.Operands.Count; i++)
                    if ((instr.Operands[i] is ValueOperand vo) && vo.Value == value)
                    {
                        instr.Operands[i] = constOperand;
                        ok = true;
                    }

                if (ok)
                    yield return instr;
            }
        }

        internal void ReplaceBlockOperand(BasicBlock target, BlockOperand newTarget)
        {
            foreach (var instr in Blocks.SelectMany(b => b.Instructions).ToList())
            {
                for (int i = 0; i < instr.Operands.Count; i++)
                    if ((instr.Operands[i] is BlockOperand bo) && bo.Block == target)
                        instr.Operands[i] = newTarget;
            }
        }

        public Method()
        {
            Blocks = new List<BasicBlock>();
            Entry = GetBlock();
        }
    }
}