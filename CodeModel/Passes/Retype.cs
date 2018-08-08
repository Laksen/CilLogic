using System;
using System.Collections.Generic;
using System.Linq;
using CilLogic.Utilities;
using Mono.Cecil;

namespace CilLogic.CodeModel.Passes
{
    public class Retype : CodePass
    {
        public override void Pass(Method method)
        {
            var retyped = new HashSet<Opcode>();
            var producers = method.AllInstructions().Where(x => x.Result != 0).ToDictionary(x => x.Result, x => x);

            Operand GetRealType(Operand oper)
            {
                if (oper is ValueOperand vo)
                {
                    var p = producers[vo.Value];
                    RetypeOp(p);

                    var newT = p.GetResultType(method);

                    return new ValueOperand(vo.Value, newT);
                }
                else if (oper is CondValue cv)
                {
                    var cond = GetRealType(cv.Condition);
                    var value = GetRealType(cv.Value);

                    return new CondValue(cond, value, value.OperandType);
                }
                else if (oper is PhiOperand po)
                {
                    var value = GetRealType(po.Value);

                    return new PhiOperand(po.Block, value);
                }
                else if (oper is ConstOperand co)
                {
                    if ((co.Value == 0) && (co.Width > 1))
                        return new ConstOperand(0, false, 1);
                    else if ((co.Value == 1) && (co.Width > 1))
                        return new ConstOperand(1, false, 1);
                    else
                        return co;
                }
                else
                    return oper;
            }

            void RetypeOp(Opcode code)
            {
                if (retyped.Contains(code)) return;

                retyped.Add(code);
                var oldOpers = code.Operands.ToList();
                code.Operands.Clear();
                code.Operands.AddRange(oldOpers.Select(GetRealType));
            }

            method.AllInstructions().ForEach(RetypeOp);

            method.IsRetyped = true;
        }
    }
}