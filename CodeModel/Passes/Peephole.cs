using System;
using System.Collections.Generic;
using System.Linq;
using CilLogic.Utilities;

namespace CilLogic.CodeModel.Passes
{
    public class PassPeephole : CodePass
    {
        public override void Pass(Method method)
        {
            DoPass<PassSinglePeephole>(method, ">");
            DoPass<PassMultiPeephole>(method, ">");
            DoPass<Diamondify>(method, ">");
            DoPass<PhiInverseDiamond>(method, ">");
        }

        public class Diamondify : CodePass
        {
            public override void Pass(Method method)
            {
                if (!method.IsSSA) return;

                bool wasOK = false;

                do
                {
                    wasOK = false;

                    var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());

                    var candidate = method.Blocks.FirstOrDefault(blk =>
                    {
                        var nb = nextBlocks[blk];
                        if (nb.Count != 2) return false;

                        var x = nb.First();
                        var y = nb.Skip(1).First();

                        void Split(BasicBlock start, BasicBlock goodPath, BasicBlock dest)
                        {
                            var newBlk = method.GetBlock();
                            newBlk.Prepend(new Opcode(0, Op.Br, new BlockOperand(dest)));

                            var jump = start.Instructions.Last();
                            for (int i = 0; i < jump.Operands.Count; i++)
                            {
                                if ((jump[i] is BlockOperand bo) && (bo.Block == dest))
                                    jump.Operands[i] = new BlockOperand(newBlk);
                            }

                            foreach (var phi in dest.Instructions.Where(ins => ins.Op == Op.Phi))
                            {
                                for (int i = 0; i < phi.Operands.Count; i++)
                                    if ((phi[i] is PhiOperand po) && (po.Block == start))
                                    {
                                        phi.Operands[i] = new PhiOperand(newBlk, po.Value);
                                        break;
                                    }
                            }
                        }

                        if ((nextBlocks[x].Count == 1) && (nextBlocks[x].First() == y) && (y != blk))
                            Split(blk, x, y);
                        else if ((nextBlocks[y].Count == 1) && (nextBlocks[y].First() == x) && (x != blk))
                            Split(blk, y, x);
                        else
                            return false;
                        return true;
                    });

                    if (candidate != null)
                        wasOK = true;
                }
                while (wasOK);
            }
        }

        public class PhiInverseDiamond : CodePass
        {
            public override void Pass(Method method)
            {
                if (!method.IsSSA) return;

                bool wasOK = false;

                do
                {
                    wasOK = false;

                    var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());
                    var candidate = method.Blocks.FirstOrDefault(b =>
                    {
                        var nb = nextBlocks[b].ToArray();
                        if (nb.Length != 2) return false;
                        if (nb[0] == nb[1]) return false;
                        if (b.Instructions.Last().Op != Op.BrCond) return false;

                        var nx = nextBlocks[nb[0]].ToArray();
                        var ny = nextBlocks[nb[1]].ToArray();

                        if (nx.Length != 1) return false;
                        if (ny.Length != 1) return false;

                        if (nx[0] != ny[0]) return false;

                        if (!nx[0].Instructions.Where(i => i.Op == Op.Phi).All(o => o.Operands.Count >= 2)) return false;

                        var cond = b.Instructions.Last()[0];

                        var b0 = b;
                        var bt = (b.Instructions.Last()[1] as BlockOperand).Block;
                        var bf = (b.Instructions.Last()[2] as BlockOperand).Block;
                        var b3 = nx[0];

                        if (bt.Instructions.Concat(bf.Instructions).Where(x => x.Op != Op.Br).Count() != 0) return false;

                        if (nextBlocks.Where(kvp => kvp.Value.Contains(bt)).Count() > 1) return false;
                        if (nextBlocks.Where(kvp => kvp.Value.Contains(bf)).Count() > 1) return false;

                        b0.Replace(b0.Instructions.Last(), new Opcode(0, Op.Br, new BlockOperand(b3)));

                        var point = b0.Instructions.Last();

                        foreach (var pi in b3.Instructions.Where(o => o.Op == Op.Phi).ToList())
                        {
                            var to = pi.Operands.OfType<PhiOperand>().Where(p => p.Block == bt).Select(x => x.Value).Single();
                            var fo = pi.Operands.OfType<PhiOperand>().Where(p => p.Block == bf).Select(x => x.Value).Single();

                            var muxOp = new Opcode(method.GetValue(), Op.Mux, cond, fo, to);
                            b0.InsertBefore(muxOp, point);

                            pi.Operands.Add(new PhiOperand(b0, new ValueOperand(muxOp.Result, to.OperandType)));

                            //b3.Replace(pi, new Opcode(pi.Result, Op.Mux, cond, Replace(fo), Replace(to)));
                        }

                        return true;
                    });

                    if (candidate != null)
                        wasOK = true;
                }
                while (wasOK);
            }
        }

        private class PhiDiamondPass : CodePass
        {
            public override void Pass(Method method)
            {
                if (!method.IsSSA) return;

                bool wasOK = false;

                do
                {
                    wasOK = false;

                    var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());
                    var candidate = method.Blocks.FirstOrDefault(b =>
                    {
                        var nb = nextBlocks[b].ToArray();
                        if (nb.Length != 2) return false;
                        if (nb[0] == nb[1]) return false;
                        if (b.Instructions.Last().Op != Op.BrCond) return false;

                        var nx = nextBlocks[nb[0]].ToArray();
                        var ny = nextBlocks[nb[1]].ToArray();

                        if (nx.Length != 1) return false;
                        if (ny.Length != 1) return false;

                        if (nx[0] != ny[0]) return false;

                        if (!nx[0].Instructions.Where(i => i.Op == Op.Phi).All(o => o.Operands.Count == 2)) return false;

                        var cond = b.Instructions.Last()[0];

                        var b0 = b;
                        var bt = (b.Instructions.Last()[1] as BlockOperand).Block;
                        var bf = (b.Instructions.Last()[2] as BlockOperand).Block;
                        var b3 = nx[0];

                        if (bt.Instructions.Concat(bf.Instructions).Where(x => x.Op != Op.Br).Any(i => i.HasSideEffects())) return false;

                        if (nextBlocks.Where(kvp => kvp.Value.Contains(bt)).Count() > 1) return false;
                        if (nextBlocks.Where(kvp => kvp.Value.Contains(bf)).Count() > 1) return false;

                        var toRep = new Dictionary<int, int>();
                        foreach (var ins in bt.Instructions.Concat(bf.Instructions).Where(x => x.Op != Op.Br))
                        {
                            var o = new Opcode(method.GetValue(), ins.Op, ins.Operands.ToArray());
                            b0.Prepend(o);
                            toRep.Add(ins.Result, o.Result);
                        }

                        Operand Replace(Operand old)
                        {
                            if (old is ValueOperand vo)
                                if (toRep.ContainsKey(vo.Value))
                                    return new ValueOperand(toRep[vo.Value], vo.OperandType);
                            return old;
                        }

                        b0.Replace(b0.Instructions.Last(), new Opcode(0, Op.Br, new BlockOperand(b3)));

                        foreach (var pi in b3.Instructions.Where(o => o.Op == Op.Phi).ToList())
                        {
                            var to = pi.Operands.OfType<PhiOperand>().Where(p => p.Block == bt).Select(x => x.Value).Single();
                            var fo = pi.Operands.OfType<PhiOperand>().Where(p => p.Block == bf).Select(x => x.Value).Single();

                            b3.Replace(pi, new Opcode(pi.Result, Op.Mux, cond, Replace(fo), Replace(to)));
                        }

                        /*foreach (var op in bt.Instructions.Where(x => x.Op != Op.Br).ToList()) { b0.Prepend(op); bt.Instructions.Remove(op); }
                        foreach (var op in bf.Instructions.Where(x => x.Op != Op.Br).ToList()) { b0.Prepend(op); bf.Instructions.Remove(op); }*/

                        //method.Blocks.Remove(bt);
                        //method.Blocks.Remove(bf);

                        return true;
                    });

                    if (candidate != null)
                    {
                        wasOK = true;
                    }
                }
                while (wasOK);
            }
        }

        private class PassMultiPeephole : CodePass
        {
            public override void Pass(Method method)
            {
                bool wasFixed;

                do
                {
                    wasFixed = false;

                    var ops = method.AllInstructions();

                    var producers = new Lazy<Dictionary<int, Opcode>>(() => ops.Where(r => r.Result != 0).ToDictionary(x => x.Result, x => x));

                    /*var usage = ops.ToDictionary(o => o, o => o.Operands.OfType<ValueOperand>().Select(x => x.Value).ToHashSet());
                    var users = ops.ToDictionary(o => o, o => ops.Where(d => usage[d].Contains(o.Result)).ToHashSet());*/

                    foreach (var op in ops)
                    {
                        switch (op.Op)
                        {
                            case Op.InSet:
                                {
                                    if ((op[0] is ValueOperand vo) && (op.Operands.Count == 2) && (op[1] is ConstOperand test) && (test.Value == 0))
                                    {
                                        var gen = producers.Value[vo.Value];

                                        if (gen.Op == Op.InSet)
                                        {
                                            var newOp = new Opcode(op.Result, Op.NInSet, gen.Operands.ToArray());
                                            op.Block.Replace(op, newOp);
                                            wasFixed = true;
                                        }
                                        else if (gen.Op == Op.NInSet)
                                        {
                                            var newOp = new Opcode(op.Result, Op.InSet, gen.Operands.ToArray());
                                            op.Block.Replace(op, newOp);
                                            wasFixed = true;
                                        }
                                    }

                                    /*if (!wasFixed && method.IsRetyped && (op[0] is ValueOperand vo2))
                                    {
                                        var gen = producers.Value[vo2.Value];

                                        if ((gen.Op == Op.Sub) && (gen[1] is ConstOperand co))
                                        {
                                            var clamp = (1UL << gen[0].OperandType.GetWidth()) - 1;

                                            var newOp = new Opcode(op.Result, Op.InSet, gen.Operands.Take(1).Concat(op.Operands.Skip(1).Cast<ConstOperand>().Select(c => new ConstOperand((c.Value + co.Value) & clamp))).ToArray());
                                            op.Block.Replace(op, newOp);
                                            wasFixed = true;
                                        }
                                    }*/
                                    break;
                                }
                            case Op.NInSet:
                                {
                                    if (method.IsRetyped && (op[0] is ValueOperand vo2))
                                    {
                                        var gen = producers.Value[vo2.Value];

                                        if ((gen.Op == Op.Sub) && (gen[1] is ConstOperand co))
                                        {
                                            var clamp = (1UL << gen[0].OperandType.GetWidth()) - 1;

                                            var newOp = new Opcode(op.Result, Op.NInSet, gen.Operands.Take(1).Concat(op.Operands.Skip(1).Cast<ConstOperand>().Select(c => new ConstOperand((c.Value + co.Value) & clamp))).ToArray());
                                            op.Block.Replace(op, newOp);
                                            wasFixed = true;
                                        }
                                    }
                                    break;
                                }
                            case Op.Slice:
                                {
                                    if ((op[0] is ValueOperand vo) && (op[1] is ConstOperand vmsb) && (op[2] is ConstOperand vlsb) && (op[3] is ConstOperand vshift) && (op[4] is ConstOperand vsign))
                                    {
                                        var gen = producers.Value[vo.Value];

                                        if ((gen.Op == Op.Slice) && (gen[1] is ConstOperand msb) && (gen[2] is ConstOperand lsb) && (gen[3] is ConstOperand shift) && (gen[4] is ConstOperand sign))
                                        {
                                            var vw = (int)(vmsb.Value - vlsb.Value + 1);
                                            var vl = (int)vlsb.Value;
                                            var vs = (int)vshift.Value;
                                            var vS = (int)vsign.Value;

                                            var w = (int)(msb.Value - lsb.Value + 1);
                                            var l = (int)lsb.Value;
                                            var s = (int)shift.Value;
                                            var S = (int)sign.Value;

                                            if ((w < vw) && (vS != S))
                                            {
                                                var newOp = new Opcode(op.Result, Op.Slice, gen[0], w + l - s + vl - 1, l - s + vl, vs, 0);
                                                op.Block.Replace(op, newOp);
                                                wasFixed = true;
                                            }
                                            else
                                            {
                                                // v = (x[l+:w]<<s)[vl+:vw]<<vs
                                                var newOp = new Opcode(op.Result, Op.Slice, gen[0], Math.Min(w, vw) + l - s + vl - 1, l - s + vl, vs, vS | S);
                                                op.Block.Replace(op, newOp);
                                                wasFixed = true;
                                            }
                                        }
                                    }
                                    break;
                                }
                            case Op.LdArray:
                                {
                                    if (op[0] is ValueOperand vo)
                                    {
                                        var gen = producers.Value[vo.Value];

                                        if (gen.Op == Op.LdFld)
                                        {
                                            op.Operands[0] = gen[1];
                                            wasFixed = true;
                                        }
                                    }
                                    break;
                                }
                            case Op.StArray:
                                {
                                    if (op[0] is ValueOperand vo)
                                    {
                                        var gen = producers.Value[vo.Value];

                                        if (gen.Op == Op.LdFld)
                                        {
                                            op.Operands[0] = gen[1];
                                            wasFixed = true;
                                        }
                                    }
                                    break;
                                }
                            case Op.BrCond:
                                {
                                    if (op[0] is ValueOperand vo)
                                    {
                                        var gen = producers.Value[vo.Value];

                                        if ((gen.Op == Op.InSet) &&
                                            (gen.Operands.Count == 2) &&
                                            (gen[1] is ConstOperand co) &&
                                            (co.Value == 0) &&
                                            (gen[0].OperandType.GetWidth() == 1))
                                        {
                                            op.Operands[0] = gen[0];

                                            var t = op[1];
                                            op.Operands[1] = op[2];
                                            op.Operands[2] = t;
                                            wasFixed = true;
                                        }
                                    }
                                    break;
                                }
                            case Op.Mux:
                                {
                                    if (op[0] is ConstOperand co)
                                    {
                                        var newOp = new Opcode(op.Result, Op.Mov, co.Value != 0 ? op[2] : op[1]);
                                        op.Block.Replace(op, newOp);
                                        wasFixed = true;
                                    }
                                    else if (op[1].Equals(op[2]))
                                    {
                                        var newOp = new Opcode(op.Result, Op.Mov, op[2]);
                                        op.Block.Replace(op, newOp);
                                        wasFixed = true;
                                    }
                                    else if ((op[2] is ConstOperand width) &&
                                        (width.Value == 1) &&
                                        (op[0] is ValueOperand cond) &&
                                        (op[1] is ValueOperand v1))
                                    {
                                        var cg = producers.Value[cond.Value];
                                        var cv = producers.Value[v1.Value];

                                        if ((cv.Op == Op.InSet) && (cg.Op == Op.InSet) && (cv[0].Equals(cg[0])))
                                        {
                                            var opSet = cg.Operands.Skip(1).Concat(cv.Operands.Skip(1)).OfType<ConstOperand>().Select(x => x.Value).ToHashSet();
                                            op.Block.Replace(op, new Opcode(op.Result, Op.InSet, new Operand[] { cv[0] }.Concat(opSet.Select(x => new ConstOperand(x))).ToArray()));
                                            wasFixed = true;
                                        }
                                        else if (cond.OperandType.IsBool() && v1.OperandType.IsBool())
                                        {
                                            op.Block.Replace(op, new Opcode(op.Result, Op.Or, cond, v1));
                                            wasFixed = true;
                                        }
                                    }
                                    else if ((op[1] is ConstOperand b) &&
                                        (b.Value == 1) &&
                                        (op[0] is ValueOperand cnd) &&
                                        (op[2] is ValueOperand v2))
                                    {
                                        var cv = producers.Value[v2.Value];

                                        if ((cv.Op == Op.InSet) && (cv[0] is ValueOperand vt) && (vt.Value == cnd.Value))
                                        {
                                            var opSet = cv.Operands.Skip(1).OfType<ConstOperand>().Select(x => x.Value).ToHashSet();
                                            opSet.Add(0);

                                            op.Block.Replace(op, new Opcode(op.Result, Op.InSet, new Operand[] { cv[0] }.Concat(opSet.Select(x => new ConstOperand(x))).ToArray()));
                                            wasFixed = true;
                                        }
                                    }

                                    if (!wasFixed)
                                    {
                                        if (op[0] is ValueOperand cnd)
                                        {
                                            var cv = producers.Value[cnd.Value];

                                            if ((cv.Op == Op.InSet) && (cv.Operands.Count == 2) &&
                                                (cv[1] is ConstOperand co2) && (co2.Value == 0) &&
                                                (cv[0].OperandType.IsBool()))
                                            {
                                                op.Block.Replace(op, new Opcode(op.Result, Op.Mux, cv[0], op[2], op[1]));
                                                wasFixed = true;
                                            }
                                        }
                                    }
                                    break;
                                }
                            case Op.Or:
                                {
                                    if ((op.Operands.Count == 2) && (op[0] is ValueOperand vo) && (op[1] is ConstOperand co2))
                                    {
                                        var gen = producers.Value[vo.Value];

                                        if ((gen.Op == Op.Or) && (gen[1] is ConstOperand v))
                                        {
                                            op.Block.Replace(op, new Opcode(op.Result, Op.Or, gen[0], co2.Value | v.Value));
                                            wasFixed = true;
                                        }
                                    }
                                    else if ((op.Operands.Count == 2) && (op[0] is ValueOperand vo1) && (op[1] is ValueOperand vo2))
                                    {
                                        var gen1 = producers.Value[vo1.Value];
                                        var gen2 = producers.Value[vo2.Value];

                                        if ((gen1.Op == Op.InSet) && (gen2.Op == Op.InSet) && (gen1[0].Equals(gen2[0])))
                                        {
                                            var values = gen1.Operands.Skip(1).Concat(gen2.Operands.Skip(1)).Cast<ConstOperand>().Select(x => x.Value).ToHashSet();

                                            op.Block.Replace(op, new Opcode(op.Result, Op.InSet, new[] { gen1[0] }.Concat(values.Select(x => new ConstOperand(x))).ToArray()));
                                            wasFixed = true;
                                        }
                                    }

                                    if (!wasFixed)
                                    {
                                        var otherOrs = op.Operands.OfType<ValueOperand>().Where(p => producers.Value[p.Value].Op == Op.Or).ToList();

                                        if (otherOrs.Any())
                                        {
                                            var old = op.Operands.ToList();
                                            op.Operands.Clear();
                                            op.Operands.AddRange(old.Except(otherOrs).Concat(otherOrs.SelectMany(x => producers.Value[x.Value].Operands)).ToHashSet().ToList());
                                            wasFixed = true;
                                        }
                                    }
                                    break;
                                }
                            case Op.And:
                                {
                                    if ((op.Operands.Count == 2) && (op[0] is ValueOperand vo1) && (op[1] is ValueOperand vo2))
                                    {
                                        var gen1 = producers.Value[vo1.Value];
                                        var gen2 = producers.Value[vo2.Value];

                                        if ((gen1.Op == Op.InSet) && (gen2.Op == Op.InSet) && (gen1[0].Equals(gen2[0])))
                                        {
                                            var values = gen1.Operands.Skip(1).Cast<ConstOperand>().ToHashSet().Intersect(gen2.Operands.Skip(1).Cast<ConstOperand>()).Select(x => x.Value).ToList();

                                            op.Block.Replace(op, new Opcode(op.Result, Op.InSet, new[] { gen1[0] }.Concat(values.Select(x => new ConstOperand(x))).ToArray()));
                                            wasFixed = true;
                                        }
                                    }

                                    if (!wasFixed)
                                    {
                                        var otherAnds = op.Operands.OfType<ValueOperand>().Where(p => producers.Value[p.Value].Op == Op.And).ToList();

                                        if (otherAnds.Any())
                                        {
                                            //var old = op.Operands.ToList();

                                            /*op.Operands.Clear();
                                            //op.Operands.AddRange(old.Except(otherAnds).Concat(otherAnds.SelectMany(x => producers.Value[x.Value].Operands)).ToHashSet().ToList());
                                            op.Operands.AddRange(old.Except(otherAnds.SelectMany(x => producers.Value[x.Value].Operands)).Distinct().ToList());*/

                                            var rem = otherAnds.SelectMany(x => producers.Value[x.Value].Operands).ToHashSet();

                                            wasFixed = op.Operands.RemoveAll(rem.Contains) > 0;
                                        }
                                    }
                                    break;
                                }
                        }

                        if (wasFixed) break;
                    }
                }
                while (wasFixed);
            }
        }

        private class PassSinglePeephole : CodePass
        {
            public override void Pass(Method method)
            {
                var queue = new Queue<Opcode>(method.AllInstructions());

                Func<Opcode, Opcode> Enqueue = e => { /*queue.Enqueue(e);*/ return e; };

                while (queue.Count > 0)
                {
                    var ins = queue.Dequeue();

                    if (ins.Block == null)
                        continue;

                    if ((ins.Operands.Count == 2) && ins.Operands.All(op => op is ConstOperand))
                    {
                        Operand o = null;
                        ConstOperand a = (ins[0] as ConstOperand), b = (ins[1] as ConstOperand);

                        switch (ins.Op)
                        {
                            case Op.And: o = new ConstOperand(a.Value & b.Value); break;
                            case Op.Or: o = new ConstOperand(a.Value | b.Value); break;
                            case Op.Xor: o = new ConstOperand(a.Value ^ b.Value); break;

                            case Op.Add: o = new ConstOperand(a.Value + b.Value); break;
                            case Op.Sub: o = new ConstOperand(a.Value - b.Value); break;

                            case Op.Lsl: o = new ConstOperand(a.Value << (int)b.Value); break;
                            case Op.Asr: o = new ConstOperand((UInt64)((Int64)(a.Value) >> (int)b.Value)); break;
                            case Op.Lsr: o = new ConstOperand(a.Value >> (int)b.Value); break;

                            case Op.Ceq: o = new ConstOperand(a.Value == b.Value ? 1UL : 0, false, 1); break;
                            case Op.Clt: o = new ConstOperand(a.SignedValue < b.SignedValue ? 1UL : 0, false, 1); break;
                            case Op.Cltu: o = new ConstOperand(a.Value < b.Value ? 1UL : 0, false, 1); break;

                            case Op.InSet: o = new ConstOperand(a.Value == b.Value ? 1UL : 0, false, 1); break;
                            case Op.NInSet: o = new ConstOperand(a.Value != b.Value ? 1UL : 0, false, 1); break;

                            case Op.Select: o = ins[0]; break;

                            case Op.StLoc: break;

                            default:
                                Console.WriteLine(ins);
                                break;
                        }

                        if (o != null)
                        {
                            var n = new Opcode(ins.Result, Op.Mov, o);
                            ins.Block.Replace(ins, n);
                            queue.Enqueue(n);
                            continue;
                        }
                    }
                    else if (ins.Operands.Where(x => !(x is TypeOperand)).All(op => op is ConstOperand))
                    {
                        Operand o = null;

                        switch (ins.Op)
                        {
                            case Op.Slice:
                                {
                                    var v = ins[0] as ConstOperand;
                                    var m = ins[1] as ConstOperand;
                                    var l = ins[2] as ConstOperand;
                                    var s = ins[3] as ConstOperand;
                                    var signed = ins[3] as ConstOperand;

                                    var width = m.Value - l.Value + 1;
                                    var mask = (width == 64) ? ~0UL : (((UInt64)1 << (int)width) - 1);

                                    o = new ConstOperand(((v.Value >> (int)l.Value) & mask) << (int)s.Value, signed.Value != 0, (int)width);
                                    break;
                                }

                            case Op.Conv:

                            case Op.LdLoc:
                            case Op.Return:
                            case Op.Mov:
                            case Op.StLoc: break;

                            case Op.Phi:
                                {
                                    if (ins.Operands.Count == 0)
                                        o = new UndefOperand();
                                    break;
                                }

                            case Op.Insert:
                                {
                                    var origin = (ins[1] as ConstOperand).Value;
                                    var m = (ins[2] as ConstOperand).Value;
                                    var l = (ins[3] as ConstOperand).Value;
                                    var v = (ins[4] as ConstOperand).Value;

                                    if (m < 64)
                                    {
                                        UInt64 mask = (1UL << (int)(m - l + 1)) - 1;

                                        o = new ConstOperand(origin | (v & mask) << (int)l, false, ins[0].OperandType.GetWidth());
                                    }
                                    break;
                                }
                            case Op.Mux:
                                if ((ins[0] is ConstOperand co4) && (co4.Value != 0))
                                    o = ins[2];
                                else
                                    o = ins[1];
                                break;

                            case Op.Select: o = ins[0]; break;

                            default:
                                Console.WriteLine(ins);
                                break;
                        }

                        if (o != null)
                        {
                            ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, o));
                            continue;
                        }
                    }
                    else if (!ins.HasSideEffects() && ins.Operands.OfType<UndefOperand>().Any())
                    {
                        ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, new UndefOperand()));
                    }

                    switch (ins.Op)
                    {
                        case Op.Mux:
                            {
                                if ((ins[1] is ConstOperand co1) && (co1.Value == 0) &&
                                    (ins[2] is ConstOperand co2) && (co2.Value == 1))
                                {
                                    var newop = new Opcode(ins.Result, Op.Mov, ins[0]);
                                    ins.Block.Replace(ins, newop);
                                    queue.Enqueue(newop);
                                }
                                else if ((ins[2] is ConstOperand co3) && (co3.Value == 0) && ins[1].OperandType.IsBool() && ins[0].OperandType.IsBool())
                                {
                                    var newop = new Opcode(ins.Result, Op.Or, ins[0], ins[1]);
                                    ins.Block.Replace(ins, newop);
                                    queue.Enqueue(newop);
                                }
                                else if ((ins[1] is ConstOperand co4) && (co4.Value == 0) && ins[2].OperandType.IsBool() && ins[0].OperandType.IsBool())
                                {
                                    var newop = new Opcode(ins.Result, Op.And, ins[0], ins[2]);
                                    ins.Block.Replace(ins, newop);
                                    queue.Enqueue(newop);
                                }
                                else if ((ins[1] is ConstOperand co5) && (co5.Value == 1) && ins[2].OperandType.IsBool() && ins[0].OperandType.IsBool())
                                {
                                    var newop = new Opcode(ins.Result, Op.Or, ins[0], ins[2]);
                                    ins.Block.Replace(ins, newop);
                                    queue.Enqueue(newop);
                                }
                                break;
                            }
                        case Op.NInSet:
                            {
                                if ((ins.Operands.Count == 2) &&
                                    (ins[1] is ConstOperand co1) && (co1.Value == 0) &&
                                    ins[0].OperandType.IsBool())
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, ins[0]));
                                break;
                            }
                        case Op.Cltu:
                            {
                                if (ins[0] is ConstOperand co)
                                {
                                    if (co.Value == 0)
                                        ins.Block.Replace(ins, new Opcode(ins.Result, Op.NInSet, ins[1], ins[0]));
                                    else if (co.Value == 1)
                                        ins.Block.Replace(ins, new Opcode(ins.Result, Op.InSet, ins[1], 0, ins[0]));
                                    /*else if (method.IsRetyped)
                                    {
                                        var maxVal = (1UL << ins[1].OperandType.GetWidth()) - 1;

                                        if (maxVal <= co.Value)
                                            ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, 0));
                                    }*/
                                }
                                break;
                            }
                        case Op.Or:
                            {
                                if (ins.Operands.Count == 1)
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, ins[0]));
                                else
                                {
                                    var oldConst = ins.Operands.OfType<ConstOperand>().FirstOrDefault();
                                    if ((oldConst != null) && ins.Operands.RemoveAll(o => (o is ConstOperand co2) && (co2.Value == 0)) > 0)
                                    {
                                        if (ins.Operands.Count == 0)
                                        {
                                            var newop = new Opcode(ins.Result, Op.Mov, oldConst);
                                            ins.Block.Replace(ins, newop);
                                            queue.Enqueue(newop);
                                        }
                                        else if (ins.Operands.Count == 1)
                                        {
                                            var newop = new Opcode(ins.Result, Op.Mov, ins[0]);
                                            ins.Block.Replace(ins, newop);
                                            queue.Enqueue(newop);
                                        }
                                    }
                                    else if (ins.Operands.OfType<ConstOperand>().Count() > 1)
                                    {
                                        var value = oldConst;

                                        foreach (var oper in ins.Operands.OfType<ConstOperand>())
                                            value = new ConstOperand(value.Value | oper.Value, value.Signed, value.Width);

                                        ins.Operands.RemoveAll(o => (o is ConstOperand));
                                        ins.Operands.Add(value);
                                        queue.Enqueue(ins);
                                    }
                                }
                                break;
                            }
                        case Op.And:
                            {
                                if (ins.Operands.Count == 1)
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, ins[0]));
                                else if (ins.Operands.Any(oper => (oper is ConstOperand co2) && (co2.Value == 0)))
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, 0));
                                else if ((ins[1] is ConstOperand co3) && (co3.Value.IsSlice(out int msb, out int lsb)))
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Slice, ins[0], msb, lsb, lsb, 0));
                                break;
                            }
                        case Op.Ceq:
                            {
                                if (ins.Operands[1] is ConstOperand co)
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.InSet, ins[0], co.Value));
                                break;
                            }
                        case Op.Conv:
                            {
                                if (ins.Operands[0] is ConstOperand co)
                                {
                                    Operand oper;

                                    var dWidth = (int)(ins[2] as ConstOperand).Value;
                                    var dSign = (ins[1] as ConstOperand).Value != 0;

                                    if (co.Signed && dSign)
                                        oper = new ConstOperand((UInt64)co.SignedValue, dSign, dWidth);
                                    else
                                        oper = new ConstOperand(co.Value, dSign, dWidth);

                                    foreach (var i2 in method.ReplaceValue(ins.Result, oper))
                                        queue.Enqueue(i2);
                                }
                                else
                                {
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Slice, ins[0], (ins[2] as ConstOperand).Value - 1, 0, 0, (ins[1] as ConstOperand).Value));
                                }
                                break;
                            }
                        case Op.Slice:
                            {
                                if (!method.IsRetyped) break;

                                if ((ins[0] is ValueOperand vo) && (ins[1] is ConstOperand vmsb) && (ins[2] is ConstOperand vlsb) && (ins[3] is ConstOperand vshift) && (ins[4] is ConstOperand vsign))
                                {
                                    var vw = (int)(vmsb.Value - vlsb.Value + 1);
                                    var vl = (int)vlsb.Value;
                                    var vs = (int)vshift.Value;
                                    var vS = (int)vsign.Value;

                                    if ((vw == vo.OperandType.GetWidth()) && (vl == 0) && (vs == 0) && ((vS != 0) == vo.OperandType.GetSigned()))
                                        ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, ins[0]));
                                    else if ((vw > vo.OperandType.GetWidth()) && (vl == 0) && (vs == 0))
                                        ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, ins[0]));
                                }
                                break;
                            }
                        case Op.BrTrue:
                            {
                                if (ins.Next.Op == Op.Br)
                                {
                                    var n = ins.Next;
                                    ins.Block.Instructions.Remove(n);
                                    ins.Block.Replace(ins, new Opcode(0, Op.BrCond, ins[0], ins[1], n[0]));
                                }
                                break;
                            }
                        case Op.BrFalse:
                            {
                                if (ins.Next.Op == Op.Br)
                                {
                                    var n = ins.Next;
                                    ins.Block.Instructions.Remove(n);
                                    ins.Block.Replace(ins, new Opcode(0, Op.BrCond, ins[0], n[0], ins[1]));
                                }
                                break;
                            }
                        case Op.Mov:
                            {
                                if ((ins[0] is ValueOperand vo) && (ins.Result == vo.Value))
                                {
                                    ins.Block.Instructions.Remove(ins);
                                    break;
                                }

                                foreach (var i2 in method.ReplaceValue(ins.Result, ins[0]))
                                    if (i2.Op != Op.Mov)
                                        queue.Enqueue(i2);

                                break;
                            }
                        case Op.Lsr:
                            {
                                if (ins[1] is ConstOperand co)
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Slice, ins[0], 63, co.Value, 0, 0));
                                break;
                            }
                        case Op.Asr:
                            {
                                if (ins[1] is ConstOperand co)
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Slice, ins[0], 63, co.Value, 0, 1));
                                break;
                            }
                        case Op.Lsl:
                            {
                                if (ins[1] is ConstOperand co2)
                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Slice, ins[0], 63 - co2.Value, 0, co2.Value, 0));
                                break;
                            }
                        case Op.Phi:
                            {
                                if (ins.Block.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().All(x => x.Block != ins.Block))
                                    ins.Operands.RemoveAll(i => (i is PhiOperand po) && po.Block == ins.Block);

                                if (ins.Operands.Count == 0)
                                    ins.Block.Replace(ins, Enqueue(new Opcode(ins.Result, Op.Mov, new UndefOperand())));
                                else if (ins.Operands.Count == 1)
                                    ins.Block.Replace(ins, Enqueue(new Opcode(ins.Result, Op.Mov, (ins.Operands[0] as PhiOperand).Value)));
                                else if (ins.Operands.OfType<PhiOperand>().Select(po => po.Value).Distinct().Count() == 1)
                                    ins.Block.Replace(ins, Enqueue(new Opcode(ins.Result, Op.Mov, (ins.Operands[0] as PhiOperand).Value)));
                                else
                                {
                                    var phis = ins.Operands.OfType<PhiOperand>().ToList();
                                    var otherThanSelf = phis.Where(x => !((x.Value is ValueOperand vo) && (vo.Value == ins.Result))).ToList();

                                    if (otherThanSelf.Count == 1)
                                        ins.Block.Replace(ins, Enqueue(new Opcode(ins.Result, Op.Mov, otherThanSelf.Single().Value)));
                                    else if (phis.Where(p => !(p.Value is UndefOperand)).All(x => (x.Value is ValueOperand vo) && vo.Value == ins.Result))
                                        ins.Block.Replace(ins, Enqueue(new Opcode(ins.Result, Op.Mov, new UndefOperand())));
                                    else if (phis.Where(p => !(p.Value is UndefOperand)).Count() == 1)
                                        ins.Block.Replace(ins, Enqueue(new Opcode(ins.Result, Op.Mov, phis.Where(p => !(p.Value is UndefOperand)).FirstOrDefault().Value)));
                                }
                                break;
                            }
                        case Op.BrCond:
                            {
                                if (ins[0] is ConstOperand co)
                                    ins.Block.Replace(ins, new Opcode(0, Op.Br, co.Value != 0 ? ins[1] : ins[2]));
                                else
                                {
                                    var targets = ins.Operands.OfType<BlockOperand>().Select(x => x.Block).ToHashSet();

                                    if (targets.Count == 1)
                                        ins.Block.Replace(ins, new Opcode(0, Op.Br, new BlockOperand(targets.First())));
                                }

                                break;
                            }
                        case Op.Select:
                            {
                                if (ins.Operands.All(x => x is CondValue))
                                {
                                    // Chose the most common value to be the default
                                    var lut = ins.Operands.Cast<CondValue>().Select(cv => cv.Value).GroupBy(x => x).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault();
                                    if (lut != null)
                                    {
                                        ins.Operands.RemoveAll(x => (x is CondValue cv) && EqualityComparer<Operand>.Default.Equals(cv.Value, lut));
                                        ins.Operands.Add(lut);
                                    }
                                }
                                else if (ins.Operands.Count == 2)
                                {
                                    var cond = ins.Operands[0] as CondValue;
                                    var def = ins[1];

                                    ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mux, cond.Condition, def, cond.Value));
                                }
                                else if (ins.Operands.Count > 1)
                                {
                                    var val = ins.Operands.Where(x => !(x is CondValue)).FirstOrDefault();
                                    ins.Operands.RemoveAll(x => (x is CondValue cv) && EqualityComparer<Operand>.Default.Equals(cv.Value, val));

                                    if (ins.Operands.Count > 2)
                                    {
                                        var otherValues = ins.Operands.OfType<CondValue>().Select(x => x.Value).ToHashSet();
                                        if (otherValues.Count == 1)
                                        {
                                            var cond = new Opcode(method.GetValue(), Op.Or, ins.Operands.OfType<CondValue>().Select(x => x.Condition).ToArray());
                                            var val2 = otherValues.Single();

                                            ins.Block.InsertBefore(cond, ins);
                                            ins.Operands.RemoveAll(x => x is CondValue);
                                            ins.Operands.Insert(0, new CondValue(new ValueOperand(cond.Result, VectorType.UInt1), val2, val2.OperandType));
                                        }
                                    }
                                }
                                else if (ins.Operands.Count == 1)
                                {
                                    var op = new Opcode(ins.Result, Op.Mov, ins[0]);
                                    ins.Block.Replace(ins, op);
                                    queue.Enqueue(op);
                                }
                                break;
                            }
                    }
                }
            }
        }
    }
}