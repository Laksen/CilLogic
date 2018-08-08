using System;
using System.Collections.Generic;
using System.Linq;
using CilLogic.CodeModel;
using CilLogic.CodeModel.Passes;
using CilLogic.Utilities;

namespace CilLogic.Scheduling
{
    public class Schedule : CodePass
    {
        internal ScheduleSettings Settings = new ScheduleSettings();

        public override void Pass(Method method)
        {
            var scheduled = new HashSet<Opcode>();
            var producers = method.AllInstructions().Where(x => x.Result != 0).ToDictionary(x => x.Result, x => x);

            int ScheduleOper(Operand oper)
            {
                if (oper is ValueOperand vo)
                {
                    Schedule(producers[vo.Value]);
                    return producers[vo.Value].ReadyOn(Settings);
                }
                else
                    return 0;
            }

            void Schedule(Opcode code)
            {
                if (scheduled.Contains(code)) return;
                if (!code.Operands.Any()) return;

                code.Schedule = code.Operands.Max(ScheduleOper);
                scheduled.Add(code);
            }

            method.AllInstructions().ForEach(Schedule);

            // Insert registers
            var regs = new Dictionary<Tuple<int, int>, int>();

            int GetReg(Opcode op, int forSchedule)
            {
                var key = new Tuple<int, int>(op.Result, forSchedule);
                if (!regs.ContainsKey(key))
                {
                    if (op.ReadyOn(Settings) == forSchedule)
                        regs[key] = op.Result;
                    else
                    {
                        var prev = GetReg(op, forSchedule - 1);
                        var reg = new Opcode(method.GetValue(), Op.Reg, new ValueOperand(prev, op.GetResultType(method)));
                        reg.Schedule = forSchedule;
                        method.Entry.Prepend(reg);
                        regs[key] = reg.Result;
                    }
                }
                return regs[key];
            }

            void DoRegister(Opcode code)
            {
                code.Operands = code.Operands.Select(x =>
                {
                    if (x is ValueOperand vo)
                    {
                        var prev = GetReg(producers[vo.Value], code.Schedule);
                        return new ValueOperand(prev, x.OperandType);
                    }
                    else
                        return x;
                }).ToList();
            }

            method.AllInstructions().ForEach(DoRegister);
        }
    }

    internal class ScheduleSettings
    {
        public int RegDelay;
        public int ArrayDelay;
        public int RequestDelay;

        public ScheduleSettings()
        {
            RegDelay = 1;
            ArrayDelay = 1;
            RequestDelay = 1;
        }
    }

    internal static class ScheduleHelpers
    {
        public static int ReadyOn(this Opcode code, ScheduleSettings settings)
        {
            switch (code.Op)
            {
                case Op.Reg: return code.Schedule + settings.RegDelay;
                case Op.LdArray: return code.Schedule + settings.ArrayDelay;
                case Op.Request: return code.Schedule + settings.RequestDelay;
                default: return code.Schedule;
            }
        }
    }

}