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
                switch (ins.Op)
                {
                    case Op.Conv:
                        {
                            if (ins.Operands[0] is ConstOperand co)
                                foreach(var i2 in method.ReplaceValue(ins.Result, new ConstOperand(co.Value, (ins.Operands[1] as ConstOperand).Value != 0, (int)(ins.Operands[1] as ConstOperand).Value)))
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
                }
            }
        }
    }
}