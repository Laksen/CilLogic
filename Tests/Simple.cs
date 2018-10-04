using System;
using CilLogic.Types;

namespace CilLogic.Tests
{
    public class SimpleActor : Actor
    {
        public IInput<int> a { get; set; }
        public IOutput<int> b { get; set; }

        public UInt64 pc;
        public UInt32 test;

        private static bool IsAligned(UInt64 value, int bitWidth)
        {
            if (bitWidth == 8) return true;
            else if (bitWidth == 16) return (value & 1) == 0;
            else if (bitWidth == 32) return (value & 3) == 0;
            else if (bitWidth == 64) return (value & 7) == 0;
            else return true;
        }

        public override void Execute()
        {
            
            var curr_pc = pc;
            var fetchError = true;

            UInt32 instr;

            var pc16bit = IsAligned(curr_pc, 16);
            var pc32bit = IsAligned(curr_pc, 32);

            if (false ? pc16bit : pc32bit)
            {
                var instrResp = (UInt32)curr_pc;

                switch(instrResp & 3)
                {
                    case 1: break;
                    case 2: break;
                    case 3: break;
                    default: fetchError = false; break;
                }

                instr = fetchError ? 2 : instrResp;
            }
            else
            {
                //errorCause = ErrorType.InstrUnaligned;
                instr = 4;
            }

            test = instr;
        }
    }
}