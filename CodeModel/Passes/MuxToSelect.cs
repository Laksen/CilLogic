using System;
using CilLogic.Utilities;

namespace CilLogic.CodeModel.Passes
{
    public class MuxToSelect : CodePass
    {
        public override void Pass(Method method)
        {
            foreach(var op in method.AllInstructions())
            {
                if (op.Op == Op.Mux)
                {
                    op.Block.Replace(op, new Opcode(op.Result, Op.Select, new CondValue(op[0], op[2], op[2].OperandType), op[1]));
                }
            }
        }
    }
}