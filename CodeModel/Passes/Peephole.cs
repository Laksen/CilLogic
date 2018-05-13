using System;
using System.Collections.Generic;
using System.Linq;

namespace CilLogic.CodeModel.Passes
{
    public class PassPeephole : CodePass
    {
        public override void Pass(Method method)
        {
            var queue = new Queue<Opcode>(method.Blocks.SelectMany(b => b.Instructions));

            while (queue.TryDequeue(out Opcode ins))
            {
                if (ins.Block == null)
                    continue;

                if ((ins.Operands.Count == 2) && ins.Operands.All(op => op is ConstOperand))
                {
                    Operand o = null;
                    ConstOperand a = (ins[0] as ConstOperand), b = (ins[1] as ConstOperand);

                    switch(ins.Op)
                    {
                        case Op.And: o = new ConstOperand(a.Value & b.Value); break;
                        case Op.Or: o = new ConstOperand(a.Value | b.Value); break;
                        case Op.Xor: o = new ConstOperand(a.Value ^ b.Value); break;
                        
                        case Op.Add: o = new ConstOperand(a.Value + b.Value); break;
                        case Op.Sub: o = new ConstOperand(a.Value - b.Value); break;
                        
                        case Op.Lsl: o = new ConstOperand(a.Value << (int)b.Value); break;
                        case Op.Asr: o = new ConstOperand((UInt64)((Int64)(a.Value) >> (int)b.Value)); break;
                        case Op.Lsr: o = new ConstOperand(a.Value >> (int)b.Value); break;
                    }

                    if (o != null)
                    {
                        ins.Block.Replace(ins, new Opcode(ins.Result, Op.Mov, o));
                        continue;
                    }
                }

                switch (ins.Op)
                {
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
                                ins.Block.InsertBefore(new Opcode(0, Op.BrCond, ins[0], ins[1], ins.Next[0]), ins);
                            break;
                        }
                    case Op.BrFalse:
                        {
                            if (ins.Next.Op == Op.Br)
                                ins.Block.InsertBefore(new Opcode(0, Op.BrCond, ins[0], ins.Next[0], ins[1]), ins);
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
                            var targets = ins.Operands.OfType<BlockOperand>().Select(x => x.Block).ToHashSet();

                            if (targets.Count == 1)
                                ins.Block.Replace(ins, new Opcode(0, Op.Br, new BlockOperand(targets.First())));

                            break;
                        }
                }
            }
        }
    }
}