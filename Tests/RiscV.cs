using System;
using CilLogic.Types;

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

        // RV32D
        fld = 0x01,
        fsd = 0x09,
    }

    public enum F3_Load : UInt32
    {
        LB = 0,
        LH = 1,
        LW = 2,
        LBU = 4,
        LHU = 5,
        LWU = 6,
        LD = 3
    }

    public enum F3_Store : UInt32
    {
        SB = 0,
        SH = 1,
        SW = 2,
        SD = 3
    }

    public enum F3_B : UInt32
    {
        Eq = 0,
        Ne = 1,
        Lt = 2,
        Ge = 3,
        Ltu = 6,
        Geu = 7
    }

    public enum F3_Alu : UInt32
    {
        Add = 0,
        Sll = 1,
        Slt = 2,
        Sltu = 3,
        Xor = 4,
        Srl = 5,
        Or = 6,
        And = 7
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
        private const UInt64 misa = 1;

        private int XLEN { get { return misa == 1 ? 64 : 32; } }

        static UInt64 E(UInt64 value, int msb, int lsb)
        {
            return (value >> lsb) & (UInt64)((1 << (msb - lsb + 1)) - 1);
        }

        static UInt64 SignExtend(UInt64 value, int msb)
        {
            return (UInt64)(((Int64)(value << (64 - msb - 1)))
             >> (64 - msb - 1));
        }

        static UInt64 AluOp(UInt64 a, UInt64 b, UInt64 f3, UInt64 f7, UInt64 shiftMask)
        {
            int shift_op = (int)(b & shiftMask);
            switch (f3 & 7)
            {
                case 0: return (f7 == 32) ? a - b : a + b;
                case 1: return a << shift_op;
                case 2: return (UInt64)(((Int64)a) < ((Int64)b) ? 1 : 0);
                case 3: return (UInt64)(a < b ? 1 : 0);
                case 4: return a ^ b;
                case 5: if (f7 == 32) return ((UInt64)(((Int64)a) >> shift_op)); else return (a >> shift_op);
                case 6: return a | b;
                case 7: return a & b;

                default: return 0;
            }
        }

        static bool Cond(UInt64 f3, UInt64 a, UInt64 b)
        {
            bool res = true;

            switch (f3 & 0x6)
            {
                case 0: res = a == b; break;
                case 4: res = ((Int64)a) < ((Int64)b); break;
                case 6: res = a < b; break;
            }

            return ((f3 & 0x1) != 0) ^ res;
        }

        private const UInt32 IllegalInstruction = 0;
        private const UInt32 R_SP = 14;

        private static UInt32 OpR(Opcode op, uint Rd, uint Rs1, uint Rs2, uint f3, uint f7)
        {
            return 0x3 | ((uint)op << 2) | (Rd << 7) | (f3 << 12) | (Rs1 << 15) | (Rs2 << 20) | (f7 << 25);
        }

        private static UInt32 OpI(Opcode op, uint Rd, uint Rs1, uint Imm, uint f3)
        {
            return 0x3 | ((uint)op << 2) | (Rd << 7) | (f3 << 12) | (Rs1 << 15) | (Imm << 20);
        }

        private static UInt32 OpS(Opcode op, uint Rs1, uint Rs2, uint Imm, uint f3)
        {
            return 0x3 | ((uint)op << 2) | ((Imm & 0x1F) << 7) | (f3 << 12) | (Rs1 << 15) | (Rs2 << 20) | ((Imm & 0xF30) << 15);
        }

        private static UInt32 OpB(Opcode op, uint Rs1, uint Rs2, uint Imm, uint f3)
        {
            return 0x3 | ((uint)op << 2) | ((Imm & 0x1F) << 7) | (f3 << 12) | (Rs1 << 15) | (Rs2 << 20) | ((Imm & 0xF30) << 15);
        }

        private static UInt32 OpU(Opcode op, uint Rd, uint Imm)
        {
            return 0x3 | ((uint)op << 2) | (Rd << 7) | (Imm & 0xFFFFF000);
        }

        private static UInt32 OpJ(Opcode op, uint Rd, uint Imm)
        {
            return 0x3 | ((uint)op << 2) | (Rd << 7) |
                (((Imm >> 20) & 0x1) << 31) |
                (((Imm >> 1) & 0x3FF) << 21) |
                (((Imm >> 11) & 0x1) << 20) |
                (((Imm >> 12) & 0xFF) << 12);
        }

        private UInt32 ExpandCompressed(UInt32 instr)
        {
            var quadrant = instr & 0x3;
            var opSelect = (instr >> 13) & 0x7;

            if (quadrant == 0x0)
            {
                UInt32 r_d_s2 = (instr >> 2) & 0x7;
                UInt32 rs1 = (instr >> 7) & 0x7;

                UInt32 uimm5376 =
                    (((instr >> 10) & 0x3) << 3) |
                    (((instr >> 5) & 0x3) << 6);
                UInt32 uimm5326 =
                    (((instr >> 10) & 0x3) << 3) |
                    (((instr >> 5) & 0x1) << 6) |
                    (((instr >> 6) & 0x1) << 2);

                switch (opSelect)
                {
                    case 0:
                        {
                            UInt32 nzuimm =
                                (((instr >> 5) & 0x1) << 3) |
                                (((instr >> 6) & 0x1) << 2) |
                                (((instr >> 7) & 0xF) << 6) |
                                (((instr >> 11) & 0x3) << 4);

                            if (nzuimm == 0)
                                break;

                            return OpI(Opcode.addi, r_d_s2, R_SP, nzuimm, (UInt32)F3_Alu.Add);
                        }; // ADDI4SPN
                    case 1: return OpI(Opcode.fld, r_d_s2, rs1, uimm5376, (UInt32)F3_Load.LD); // FLD
                    case 2: return OpI(Opcode.ld, r_d_s2, rs1, uimm5326, (UInt32)F3_Load.LW); // LW
                    case 3: return OpI(Opcode.ld, r_d_s2, rs1, uimm5376, (UInt32)F3_Load.LD); // LD
                    case 5: return OpS(Opcode.fsd, rs1, r_d_s2, uimm5376, (UInt32)F3_Store.SD); // FSD
                    case 6: return OpS(Opcode.sd, rs1, r_d_s2, uimm5326, (UInt32)F3_Store.SW); // SW
                    case 7: return OpS(Opcode.sd, rs1, r_d_s2, uimm5376, (UInt32)F3_Store.SD); // SD
                }
            }
            else if (quadrant == 0x1)
            {
                UInt32 rd1_full = (instr >> 7) & 0x1F;
                UInt32 rs1 = (instr >> 7) & 0x07;

                UInt32 uimm540 =
                    (((instr >> 12) & 0x1) << 5) |
                    ((instr >> 2) & 0x1F);
                var imm540 = uimm540;

                var imm_16sp = 
                    (((instr >> 12) & 0x1) << 9) |
                    (((instr >> 6) & 0x1) << 4) |
                    (((instr >> 5) & 0x1) << 6) |
                    (((instr >> 3) & 0x3) << 7) |
                    (((instr >> 2) & 0x1) << 5);

                var imm17 = 
                    (((instr >> 12) & 0x1) << 17) |
                    (((instr >> 2) & 0x1F) << 12);
                    
                var imm_j = 
                    (((instr >> 12) & 0x1) << 11) |
                    (((instr >> 11) & 0x1) << 4) |
                    (((instr >> 9) & 0x3) << 8) |
                    (((instr >> 8) & 0x1) << 10) |
                    (((instr >> 7) & 0x1) << 6) |
                    (((instr >> 6) & 0x1) << 7) |
                    (((instr >> 3) & 0x7) << 1) |
                    (((instr >> 2) & 0x1) << 5);

                var imm_b = 
                    (((instr >> 12) & 0x1) << 8) |
                    (((instr >> 10) & 0x3) << 3) |
                    (((instr >> 5) & 0x3) << 6) |
                    (((instr >> 3) & 0x3) << 1) |
                    (((instr >> 2) & 0x1) << 5);

                switch (opSelect)
                {
                    case 0: return OpI(Opcode.addi, rd1_full, rd1_full, uimm540, (uint)F3_Alu.Add); // ADDI
                    case 1: return OpI(Opcode.addiw, rd1_full, rd1_full, imm540, (uint)F3_Alu.Add); // ADDIW
                    case 2: return OpI(Opcode.addi, rd1_full, 0, imm540, (uint)F3_Alu.Add); // LI
                    case 3: 
                        if(rd1_full == 2)
                            return OpI(Opcode.addi, 2, 2, imm_16sp, (uint)F3_Alu.Add); // ADDI16SP
                        else
                            return OpI(Opcode.addi, rd1_full, 0, imm17, (uint)F3_Alu.Add); // LUI
                    case 4: break; // MISC-ALU TODO
                    case 5: return OpJ(Opcode.jal, 0, imm_j); // J
                    case 6: return OpB(Opcode.beq, rs1, 0, imm_b, (uint)F3_B.Eq); // BEQZ
                    case 7: return OpB(Opcode.beq, rs1, 0, imm_b, (uint)F3_B.Ne); // BNEZ
                }
            }
            else if (quadrant == 0x2)
            {
                switch (opSelect)
                {
                    case 0: break; // SLLI
                    case 1: break; // FLDSP
                    case 2: break; // LWSP
                    case 3: break; // LDSP
                    case 4: break; // J[AL]R/MV/ADD
                    case 5: break; // FSDSP
                    case 6: break; // SWSP
                    case 7: break; // SDSP
                }
            }

            return instr;
        }

        public override void Execute()
        {
            var instr = Instr.Read(this);
            var curr_pc = pc;
            var npc = curr_pc + 4;

            //instr = ExpandCompressed(instr);

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

            var isR = (opcode == Opcode.add) || (opcode == Opcode.addw);
            var isI = (opcode == Opcode.addi) || (opcode == Opcode.addiw);

            var isW = (opcode == Opcode.addw) || (opcode == Opcode.addiw);

            var op2   = isR ? vrs2 : imm;
            var aluf7 = isR ? f7 : 0;
            var aluf3 = isR || isI ? f3 : 0;

            // Execute
            UInt64 result = AluOp(vrs1, op2, aluf3, aluf7, (UInt64)(isW ? 0x1F : 0x3F));

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