using System;
using System.Collections.Generic;
using System.Linq;
using CilLogic.Utilities;

namespace CilLogic.CodeModel.Passes
{
    internal class InstrComparer : IEqualityComparer<Opcode>
    {
        public bool Equals(Opcode x, Opcode y)
        {
            if (x.Op != y.Op) return false;
            if (x.Schedule != y.Schedule) return false;
            if (x.Operands.Count != y.Operands.Count) return false;

            for (int i = 0; i < x.Operands.Count; i++)
                if (!x[i].Equals(y[i]))
                    return false;

            return true;
        }

        public int GetHashCode(Opcode obj)
        {
            return obj.Op.GetHashCode() ^
                obj.Operands.Sum(x => x.GetHashCode());
        }
    }

    public class ReuseDuplicates : CodePass
    {
        public override void Pass(Method method)
        {
            bool wasFixed = true;
            while (wasFixed)
            {
                wasFixed = false;

                var x2 = method.Entry.Instructions.ToLookup(x => x, new InstrComparer());

                foreach (var instr in method.Entry.Instructions.Where(o => o.Op != Op.Mov).ToList())
                {
                    var lut = x2[instr];
                    var lf = lut.First();

                    if (lf != instr)
                    {
                        //Console.WriteLine(instr);
                        method.ReplaceValue(instr.Result, new ValueOperand(lf));
                        instr.Block.Replace(instr, new Opcode(instr.Result, Op.Mov, new ValueOperand(lf)));
                        wasFixed = true;
                        break;
                    }
                }
            }
        }
    }

    public class CollapseControlFlow : CodePass
    {
        public override void Pass(Method method)
        {
            var newBlock = method.GetBlock();

            foreach (var blk in method.Blocks)
            {
                if (blk == newBlock) continue;

                foreach (var instr in blk.Instructions.Reverse<Opcode>().ToList())
                {
                    switch (instr.Op)
                    {
                        case Op.Br:
                        case Op.BrCond:
                        case Op.Switch:
                            continue;
                    }

                    newBlock.Prepend(instr);
                }
            }

            newBlock.Instructions.Sort((x, y) =>
                x.IsTerminating() && y.IsTerminating() ? 0 :
                x.IsTerminating() ? 1 :
                y.IsTerminating() ? -1 :
                0);

            method.Blocks.RemoveAll(x => x != newBlock);
            method.Entry = newBlock;
        }
    }

    public class InlineConditions : CodePass
    {
        public override void Pass(Method method)
        {
            var next = method.Blocks.ToDictionary(b => b, b => b.NextBlocks());
            var pred = new Dictionary<BasicBlock, HashSet<BasicBlock>>();

            var conditions = new Dictionary<BasicBlock, int>();
            var cnd = new Opcode(method.GetValue(), Op.Mov, 1);
            conditions[method.Entry] = cnd.Result;
            method.Entry.Prepend(cnd);

            var toCondition = new Queue<BasicBlock>(method.Blocks);

            foreach (var b in next)
                foreach (var n in b.Value)
                {
                    if (!pred.ContainsKey(n))
                        pred[n] = new HashSet<BasicBlock>();
                    pred[n].Add(b.Key);
                }

            int GetTargetCondition(BasicBlock src, BasicBlock dst)
            {
                if (!src.Instructions.Any(x => x.IsCondJump)) return conditions[src];

                var d = new HashSet<int>();

                foreach (var x in src.Instructions)
                {
                    if (x.Op == Op.Switch)
                    {
                        var test = x.Operands[0];
                        var indices = Enumerable.Range(0, x.Operands.Count - 1).Where(i => (x[i + 1] is BlockOperand bo) && (bo.Block == dst)).ToList();

                        int res = 0;

                        // (test in indices)
                        if (indices.Count != 0)
                        {
                            var testOp = src.Prepend(new Opcode(method.GetValue(), Op.InSet, new[] { test }.Concat(indices.Select(y => new ConstOperand(y))).ToArray()));

                            res = testOp.Result;
                        }

                        if (x.Next != null)
                        {
                            var br = x.Next;

                            if ((br[0] is BlockOperand bo) && (bo.Block == dst))
                            {
                                // (test > (x.Operands.Count-1))
                                var testOp = src.Prepend(new Opcode(method.GetValue(), Op.Cltu, x.Operands.Count - 2, test));

                                if (res != 0)
                                    res = src.Prepend(new Opcode(method.GetValue(), Op.Or, new ValueOperand(testOp), new ValueOperand(res, VectorType.UInt1))).Result;
                                else
                                    res = testOp.Result;
                            }
                        }

                        res = src.Prepend(new Opcode(method.GetValue(), Op.And, new ValueOperand(res, VectorType.UInt1), new ValueOperand(conditions[src], VectorType.UInt1))).Result;

                        return res;
                    }
                    else if (x.Op == Op.BrCond)
                    {
                        int res = 0;

                        if ((x[1] is BlockOperand bo) && (bo.Block == dst))
                            res = src.Prepend(new Opcode(method.GetValue(), Op.NInSet, x[0], 0)).Result;
                        else if ((x[2] is BlockOperand bo2) && (bo2.Block == dst))
                            res = src.Prepend(new Opcode(method.GetValue(), Op.InSet, x[0], 0)).Result;

                        res = src.Prepend(new Opcode(method.GetValue(), Op.And, new ValueOperand(res, VectorType.UInt1), new ValueOperand(conditions[src], VectorType.UInt1))).Result;

                        return res;
                    }
                }

                return 0;
            }

            void GetCondition(BasicBlock b)
            {
                if (conditions.ContainsKey(b)) return;

                foreach (var p in pred[b])
                    GetCondition(p);

                var conds = pred[b].ToDictionary(p => p, p => GetTargetCondition(p, b));

                conditions[b] = b.Prepend(new Opcode(method.GetValue(), Op.Or, conds.Values.Select(x => new ValueOperand(x, VectorType.UInt1)).ToArray())).Result;

                foreach (var phi in b.Instructions.Where(i => i.Op == Op.Phi).ToList())
                    b.Replace(phi, new Opcode(phi.Result, Op.Select, phi.Operands.OfType<PhiOperand>().Select(o => new CondValue(new ValueOperand(conds[o.Block], VectorType.UInt1), o.Value, o.Value.OperandType)).ToArray()));
            }

            while (toCondition.Count > 0)
                GetCondition(toCondition.Dequeue());

            foreach (var x in method.AllInstructions())
            {
                switch (x.Op)
                {
                    case Op.Return:

                    case Op.StFld:
                    case Op.StArray:
                    
                    case Op.LdArray:
                    case Op.LdFld:

                    case Op.Request:

                    case Op.ReadPort:
                    case Op.WritePort:
                        x.Operands.Add(new ValueOperand(conditions[x.Block], VectorType.UInt1));
                        break;
                }
            }
        }
    }
}