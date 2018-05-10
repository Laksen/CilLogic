

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

        BrFalse, BrTrue, Br,
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

        public override string ToString()
        {
            return "  " + (Result != 0 ? "%" + Result + " = " : "") + Op + " " + string.Join(", ", Operands);
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
        private int counter = 1;

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

        public BasicBlock SplitBefore(Opcode instruction)
        {
            var idx = Instructions.IndexOf(instruction);
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

        private static int ValueCounter;

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

        public Method()
        {
            Blocks = new List<BasicBlock>();
            Entry = GetBlock();
        }
    }
}