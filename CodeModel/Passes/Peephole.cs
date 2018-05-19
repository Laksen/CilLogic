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
            SingleOpPass(method);
            MultiOpPass(method);
        }

        private void MultiOpPass(Method method)
        {
            bool wasFixed;

            do
            {
                wasFixed = false;

                var ops = method.AllInstructions();

                /*var usage = ops.ToDictionary(o => o, o => o.Operands.OfType<ValueOperand>().Select(x => x.Value).ToHashSet());
                var users = ops.ToDictionary(o => o, o => ops.Where(d => usage[d].Contains(o.Result)).ToHashSet());*/

                foreach (var op in ops)
                {
                    switch (op.Op)
                    {
                        case Op.Slice:
                            {
                                if ((op[0] is ValueOperand vo) && (op[1] is ConstOperand vmsb) && (op[2] is ConstOperand vlsb) && (op[3] is ConstOperand vshift) && (op[4] is ConstOperand vsign))
                                {
                                    var gen = ops.Where(x => x.Result == vo.Value).FirstOrDefault();
                                    if (gen == null) break;

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
                                            break;

                                        // v = (x[l+:w]<<s)[vl+:vw]<<vs

                                        var newOp = new Opcode(op.Result, Op.Slice, gen[0], Math.Min(w,vw)+l-s+vl-1, l-s+vl, vs, vS | S);
                                        op.Block.Replace(op, newOp);
                                        wasFixed = true;
                                    }
                                }
                                break;
                            }
                        case Op.Conv:
                            {
                                if ((op[2] is ConstOperand width) && (op[1] is ConstOperand sign))
                                {
                                    var newOp = new Opcode(op.Result, Op.Slice, op[0], (int)width.Value - 1, 0, 0, sign);
                                    op.Block.Replace(op, newOp);
                                    wasFixed = true;
                                }
                                break;
                            }
                        case Op.Lsr:
                            {
                                if (op[1] is ConstOperand co)
                                {
                                    var newOp = new Opcode(op.Result, Op.Slice, op[0], 63, co.Value, 0, 0);
                                    op.Block.Replace(op, newOp);
                                    wasFixed = true;
                                }
                                break;
                            }
                        case Op.Asr:
                            {
                                if (op[1] is ConstOperand co)
                                {
                                    var newOp = new Opcode(op.Result, Op.Slice, op[0], 63, co.Value, 0, 1);
                                    op.Block.Replace(op, newOp);
                                    wasFixed = true;
                                }
                                break;
                            }
                        case Op.Or:
                            {
                                if ((op[0] is ValueOperand vo) && (op[1] is ConstOperand co2))
                                {
                                    var gen = ops.Where(x => x.Result == vo.Value).FirstOrDefault();
                                    if (gen == null) break;

                                    if ((gen.Op == Op.Or) && (gen[1] is ConstOperand v))
                                    {
                                        op.Block.Replace(op, new Opcode(op.Result, Op.Or, gen[0], co2.Value | v.Value));
                                        wasFixed = true;
                                    }
                                }
                                break;
                            }
                        case Op.And:
                            {
                                if ((op[0] is ValueOperand vo) && (op[1] is ConstOperand co2) && ((co2.Value + 1).IsPot(out int bits)))
                                {
                                    var gen = ops.Where(x => x.Result == vo.Value).FirstOrDefault();
                                    if (gen == null) break;

                                    if ((gen.Op == Op.Slice) && (gen[1] is ConstOperand msb) && (gen[2] is ConstOperand lsb) && (gen[3] is ConstOperand shift) && (shift.Value == 0))
                                    {
                                        op.Block.Replace(op, new Opcode(op.Result, Op.Slice, gen[0], Math.Min(msb.Value, (UInt64)(lsb.Value + (UInt64)bits)), lsb.Value, 0, gen[4]));
                                        wasFixed = true;
                                    }
                                    else
                                    {
                                        op.Block.Replace(op, new Opcode(op.Result, Op.Slice, op[0], bits, 0, 0, 0));
                                        wasFixed = true;
                                    }
                                }
                                break;
                            }
                        case Op.Lsl:
                            {
                                if ((op[0] is ValueOperand vo) && (op[1] is ConstOperand co2))
                                {
                                    var gen = ops.Where(x => x.Result == vo.Value).FirstOrDefault();
                                    if (gen == null) break;

                                    if ((gen.Op == Op.Slice) && (gen[1] is ConstOperand msb) && (gen[2] is ConstOperand lsb) && (gen[3] is ConstOperand shift))
                                    {
                                        op.Block.Replace(op, new Opcode(op.Result, Op.Slice, gen[0], msb.Value, lsb.Value, co2.Value, gen[4]));
                                        wasFixed = true;
                                    }
                                    else
                                    {
                                        op.Block.Replace(op, new Opcode(op.Result, Op.Slice, op[0], 63 - co2.Value, 0, co2.Value, 0));
                                        wasFixed = true;
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

        private void SingleOpPass(Method method)
        {
            var queue = new Queue<Opcode>(method.AllInstructions());

            while (queue.TryDequeue(out Opcode ins))
            {
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

                        case Op.Ceq: o = new ConstOperand(a.Value == b.Value ? 1 : 0); break;
                        case Op.Clt: o = new ConstOperand(a.SignedValue < b.SignedValue ? 1 : 0); break;
                        case Op.Cltu: o = new ConstOperand(a.Value < b.Value ? 1 : 0); break;

                        case Op.StLoc: break;

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
                else if (ins.Operands.All(op => op is ConstOperand))
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

                                o = new ConstOperand(((v.Value >> (int)l.Value) & (((UInt64)1 << (int)(m.Value - l.Value)) - 1)) << (int)s.Value, signed.Value != 0, (int)(m.Value - l.Value + 1));
                                break;
                            }

                        case Op.Conv:

                        case Op.LdLoc:
                        case Op.Return:
                        case Op.Mov:
                        case Op.StLoc: break;

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

                switch (ins.Op)
                {
                    case Op.Or:
                        {
                            if ((ins[1] is ConstOperand co) && (co.Value == 0))
                            {
                                var newop = new Opcode(ins.Result, Op.Mov, ins[0]);
                                ins.Block.Replace(ins, newop);
                                queue.Enqueue(newop);
                            }
                            else if (ins[0] is ConstOperand)
                            {
                                var newop = new Opcode(ins.Result, Op.Or, ins[1], ins[0]);
                                ins.Block.Replace(ins, newop);
                                queue.Enqueue(newop);
                            }
                            break;
                        }
                    case Op.Conv:
                        {
                            if (ins.Operands[0] is ConstOperand co)
                                foreach (var i2 in method.ReplaceValue(ins.Result, new ConstOperand(co.Value, (ins.Operands[1] as ConstOperand).Value != 0, (int)(ins.Operands[1] as ConstOperand).Value)))
                                    queue.Enqueue(i2);
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
                                queue.Enqueue(i2);

                            break;
                        }
                    case Op.Phi:
                        {
                            if (ins.Block.Instructions.Last().Operands.OfType<BlockOperand>().All(x => x.Block != ins.Block))
                            {
                                ins.Operands.RemoveAll(i => (i is PhiOperand po) && po.Block == ins.Block);
                            }

                            var phis = ins.Operands.OfType<PhiOperand>().ToList();

                            if (phis.Select(po => po.Value).Distinct().Count() == 1)
                                ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, phis.FirstOrDefault().Value));
                            else if (phis.Count == 1)
                                ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, phis[0].Value));
                            else if (phis.Where(p => !(p.Value is UndefOperand)).All(x => (x.Value is ValueOperand vo) && vo.Value == ins.Result))
                                ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, new UndefOperand()));
                            else if (phis.Where(p => !(p.Value is UndefOperand)).Count() == 1)
                                ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, phis.Where(p => !(p.Value is UndefOperand)).FirstOrDefault().Value));

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
                }
            }
        }
    }
}