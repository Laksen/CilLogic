using System;

namespace CilLogic.Tests
{
    public enum Opcode
    {
        // RV32I
        lui = 0x0D,
        auipc = 0x05,
        jal = 0x1b,
        jalr = 0x19,
        beq = 0x18,
        ld = 0x00,
        sd = 0x08,
        addi = 0x04,
        add = 0x0C,
        fence = 0x03,

        // RV64I
        addiw = 0x06,
        addw = 0x0E,
    }

    public class RiscV : Actor
    {
        public IInput<UInt32> Instr { get; set; }
        public IOutput<UInt64> Jump { get; set; }

        public IOutput<int> DataReq { get; set; }
        public IInput<int> DataResp { get; set; }

        private UInt64[] regs;

        private UInt64 pc = 0;
        private UInt64 mstatus = 0;
        private UInt64 misa = 0;

        private int XLEN { get { return misa == 1 ? 64 : 32; } }

        static UInt64 E(UInt64 value, int msb, int lsb)
        {
            return (value >> lsb) & (UInt64)((1 << (msb - lsb + 1)) - 1);
        }

        static UInt64 SignExtend(UInt64 value, int msb)
        {
            return (UInt64)(((Int64)(value << (64 - msb)))
             >> (64 - msb));
        }

        static UInt64 AluOp(UInt64 a, UInt64 b, UInt64 f3, UInt64 f7, UInt64 shiftMask)
        {
            switch (f3)
            {
                case 0: return (f7 == 32) ? a - b : a + b;
                case 1: return a << (int)(b & shiftMask);
                case 2: return (UInt64)(((Int64)a) < ((Int64)b) ? 1 : 0);
                case 3: return (UInt64)(a < b ? 1 : 0);
                case 4: return a ^ b;
                case 5: return (f7 == 32) ? ((UInt64)(((Int64)a) >> (int)(b & shiftMask))) : (a >> (int)b);
                case 6: return a | b;
                default: return a & b;
            }
        }

        static bool Cond(UInt64 f3, UInt64 a, UInt64 b)
        {
            bool res = true;

            switch (f3 & 0x6)
            {
                case 0: return a == b;
                case 4: return ((Int64)a) < ((Int64)b);
                case 6: return a < b;
            }

            return ((f3 & 0x1) != 0) ^ res;
        }

        public override void Execute()
        {
            var instr = Instr.Read(this);
            var curr_pc = pc;
            var npc = curr_pc + 4;

            // Set the most likely outcome
            pc = npc;

            // Decode instruction
            var opcode = (Opcode)((instr >> 2) & 0x1F);
            var rd = E(instr, 11, 7);
            var rs1 = E(instr, 19, 15);
            var rs2 = E(instr, 24, 20);
            var f3 = E(instr, 14, 12);
            var f7 = E(instr, 31, 25);

            var imm = (UInt64)0;

            var status = mstatus;
            var vrs1 = regs[rs1];
            var vrs2 = regs[rs2];

            var op2 = (opcode == Opcode.add) || (opcode == Opcode.addw) ? vrs2 : imm;
            var aluf7 = (opcode == Opcode.add) || (opcode == Opcode.addw) ? f7 : 0;
            var aluf3 = (opcode == Opcode.add) || (opcode == Opcode.addw) || (opcode == Opcode.addi) || (opcode == Opcode.addiw) ? f3 : 0;

            // Execute
            UInt64 result = AluOp(vrs1, op2, aluf3, aluf7, (UInt64)((opcode == Opcode.addiw) || (opcode == Opcode.addw) ? 0x1F : 0x3F));

            switch (opcode)
            {
                case Opcode.add:
                case Opcode.addi: break;

                case Opcode.addw:
                case Opcode.addiw:
                    result = SignExtend(result, 31);
                    break;

                case Opcode.lui: result = imm; break;
                case Opcode.auipc: result = curr_pc + imm; break;
                case Opcode.jal:
                case Opcode.jalr:
                    {
                        var calc_pc = curr_pc + (opcode == Opcode.jal ? imm : result);
                        Jump.Write(calc_pc, this);
                        result = npc;
                        pc = calc_pc;
                        break;
                    }

                case Opcode.beq:
                    {
                        if (Cond(f3, vrs1, vrs2))
                        {
                            var calc_pc = curr_pc + imm;
                            Jump.Write(calc_pc, this);
                            pc = calc_pc;
                        }
                        rd = 0;
                        break;
                    }

                case Opcode.ld:
                    {
                        break;
                    }

                case Opcode.sd:
                    {
                        rd = 0;
                        break;
                    }

                case Opcode.fence:
                    {
                        // NOP
                        rd = 0;
                        break;
                    }
            }

            if (rd != 0) regs[rd] = result;
        }

        public RiscV()
        {
            regs = new UInt64[32];
        }
    }
}