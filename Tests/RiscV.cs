using System;
using CilLogic.Types;

namespace CilLogic.Tests
{
    public class RiscV : Actor
    {
        private const bool UseM = false;
        private const bool UseA = false;
        private const bool UseF = false;
        private const bool UseD = false;
        private const bool UseC = false;

        private const bool IncludeRVFI = false;

        [PortPrefix("IMem")]
        public IRequest<UInt64, InstrResponse> IMem;

        [PortPrefix("DMem")]
        public IRequest<MemRequest, MemResponse> DMem;

        [PortPrefix("rvfi")]
        public IOutput<RVFIPacket> RVFI;

        [ArrayLength(32)]
        private UInt64[] regs;
        [ResetValue(0)]
        private UInt64 pc;
        private PrivilegeLevel privilege = PrivilegeLevel.M;

        public const UInt64 mvendorid = 0;
        public const UInt64 marchid = 0;
        public const UInt64 mimpid = 0;
        public const UInt64 mhartid = 0;
        [ResetValue(0x0000_0022_0000_0000)]
        public UInt64 mstatus;
        public const UInt64 misa = 0x8000_0000_0000_0000;
        public const UInt64 medeleg = 0;
        public const UInt64 mideleg = 0;
        public UInt64 mie;
        public UInt64 mtvec;
        public UInt64 mcounteren;
        public UInt64 mscratch;
        public UInt64 mepc;
        public UInt64 mcause;
        public UInt64 mtval;
        public UInt64 mip;
        public UInt64 mcycle;
        public UInt64 minstret;

        public CSRResult WriteRegister(UInt64 addr, UInt64 clearMask, UInt64 setMask, bool write, bool read, bool is64)
        {
            UInt64 res = 0;

            if ((addr & 0xFF0) == 0xF10)
                switch (addr & 0xF)
                {
                    case 0x0: res = mvendorid; break;
                    case 0x1: res = marchid; break;
                    case 0x2: res = mimpid; break;
                    case 0x3: res = mhartid; break;
                }
            else if ((addr & 0xFF0) == 0x300)
                switch (addr & 0xF)
                {
                    case 0x0: res = mstatus; break;
                    case 0x1: res = misa; break;
                    case 0x2: res = medeleg; break;
                    case 0x3: res = mideleg; break;
                    case 0x4: res = mie; break;
                    case 0x5: res = mtvec; break;
                    case 0x6: res = mcounteren; break;
                }
            else if ((addr & 0xFF0) == 0x340)
                switch (addr & 0xF)
                {
                    case 0x0: res = mscratch; break;
                    case 0x1: res = mepc; break;
                    case 0x2: res = mcause; break;
                    case 0x3: res = mtval; break;
                    case 0x4: res = mip; break;
                }
            else
                return new CSRResult { Ok = false, OldValue = 0 };

            var old = res;
            res = (res & clearMask) | setMask;
            var ok = true;

            if (write)
            {
                if ((addr & 0xFF0) == 0x300)
                    switch (addr & 0xF)
                    {
                        case 0x0:
                            {
                                if (E(res, 12, 11) == 2)
                                    ok = false;
                                else
                                    mstatus = (mstatus & 0x3FFF_FFFF_FF80_0644UL) | (res & ~0x3FFF_FFFF_FF80_0644UL);
                                break;
                            }
                        case 0x1:
                            {
                                //misa = res;
                                break;
                            }
                        case 0x4:
                            mie = (mie & 0x444) | (res & ~0x444UL);
                            break;
                        case 0x5: mtvec = res; break;
                        case 0x6: mcounteren = res; break;
                        default: ok = false; break;
                    }
                else if ((addr & 0xFF0) == 0x340)
                    switch (addr & 0xF)
                    {
                        case 0x0: mscratch = res; break;
                        case 0x1: mepc = res; break;
                        case 0x2: mcause = res; break;
                        case 0x3: mtval = res; break;
                        case 0x4: mip = res & 0xBBBUL; break;
                        default: ok = false; break;
                    }
                else
                    ok = false;
            }

            return new CSRResult { Ok = ok, OldValue = old };
        }

        internal void Trap()
        {

        }

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
            bool hasF7bit = ((f7 >> 5) & 1) != 0;

            switch (f3 & 7)
            {
                case 0: return hasF7bit ? a - b : a + b;
                case 1: return a << shift_op;
                case 2: return (UInt64)(((Int64)a) < ((Int64)b) ? 1 : 0);
                case 3: return (UInt64)(a < b ? 1 : 0);
                case 4: return a ^ b;
                case 5: if (hasF7bit) return ((UInt64)(((Int64)a) >> shift_op)); else return (a >> shift_op);
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

        private const UInt32 R_RA = 1;
        private const UInt32 R_SP = 2;

        private static bool IsAligned(UInt64 value, int bitWidth)
        {
            if (bitWidth == 8) return true;
            else if (bitWidth == 16) return (value & 1) == 0;
            else if (bitWidth == 32) return (value & 3) == 0;
            else if (bitWidth == 64) return (value & 7) == 0;
            else return true;
        }

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
                        if (rd1_full == 2)
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
                UInt32 rd1_full = (instr >> 7) & 0x1F;
                UInt32 r_d_s2 = (instr >> 2) & 0x7;

                UInt32 nzuimm540 =
                    (((instr >> 12) & 0x1) << 5) |
                    ((instr >> 2) & 0x1F);

                var uimm5386 =
                    (((instr >> 10) & 0x7) << 3) |
                    (((instr >> 7) & 0x7) << 6);
                var uimm5276 =
                    (((instr >> 9) & 0xF) << 2) |
                    (((instr >> 7) & 0x3) << 6);
                var uimm54386 =
                    (((instr >> 12) & 0x1) << 5) |
                    (((instr >> 5) & 0x3) << 3) |
                    (((instr >> 2) & 0x7) << 6);
                var uimm54276 =
                    (((instr >> 12) & 0x1) << 5) |
                    (((instr >> 4) & 0x7) << 2) |
                    (((instr >> 2) & 0x3) << 6);

                switch (opSelect)
                {
                    case 0: return OpI(Opcode.addi, rd1_full, rd1_full, nzuimm540, (uint)F3_Alu.Sll); // SLLI
                    case 1: return OpI(Opcode.fld, rd1_full, R_SP, uimm54386, (UInt32)F3_Load.LD); // FLDSP
                    case 2: if (rd1_full != 0) return OpI(Opcode.ld, rd1_full, R_SP, uimm54276, (UInt32)F3_Load.LW); break; // LWSP
                    case 3: if (rd1_full != 0) return OpI(Opcode.ld, rd1_full, R_SP, uimm54386, (UInt32)F3_Load.LD); break; // LDSP
                    case 4: // J[AL]R/MV/ADD
                        switch ((instr >> 12) & 1)
                        {
                            case 0:
                                if (r_d_s2 == 0)
                                {
                                    if (rd1_full != 0) return OpI(Opcode.jalr, 0, rd1_full, 0, 0);
                                }
                                else
                                    return OpI(Opcode.addi, rd1_full, r_d_s2, 0, (uint)F3_Alu.Sll);
                                break;
                            default:
                                if (r_d_s2 == 0)
                                {
                                    if (rd1_full == 0)
                                        return OpI(Opcode.sys, 0, 0, 1, 0); // EBREAK
                                    else
                                        return OpI(Opcode.jalr, R_RA, rd1_full, 0, 0);
                                }
                                else
                                    return OpR(Opcode.add, rd1_full, rd1_full, r_d_s2, (uint)F3_Alu.Add, 0);
                        }
                        break;
                    case 5: return OpS(Opcode.fsd, R_SP, r_d_s2, uimm5386, (UInt32)F3_Load.LD); // FLDSP
                    case 6: return OpS(Opcode.sd, R_SP, r_d_s2, uimm5276, (UInt32)F3_Load.LW); // SWSP
                    case 7: return OpS(Opcode.sd, R_SP, r_d_s2, uimm5386, (UInt32)F3_Load.LD); // SDSP
                }
            }

            return instr;
        }

        private UInt64 GetSize(UInt32 instr)
        {
            switch (instr & 0x3)
            {
                case 0:
                case 1:
                case 2:
                    return 2;
                default:
                    return 4;
            }
        }

        public override void Execute()
        {
            var curr_pc = pc;
            var curr_priv = privilege;

            bool wasTrap = false;
            bool wasIntr = false;

            UInt64 tval = curr_pc;
            var errorCause = ErrorType.IllegalInstruction;
            var fetchError = true;

            UInt32 instr;

            var curr_misa = misa;

            var hasC = E(curr_misa, 2, 2) != 0;

            var xlen = E(curr_misa, 63, 62) == 2 ? 64 : 32;
            var is64 = xlen == 64;

            var addrSpaceMask = is64 ? 0xFFFF_FFFFUL : 0xFFFF_FFFF_FFFF_FFFF;

            var pc16bit = IsAligned(curr_pc, 16);
            var pc32bit = IsAligned(curr_pc, 32);

            if (hasC ? pc16bit : pc32bit)
            {
                var instrResp = IMem.Request(curr_pc & addrSpaceMask);
                switch (instrResp.Status)
                {
                    case MemStatus.BusError:
                    case MemStatus.DeviceError: errorCause = ErrorType.InstrAccess; break;
                    case MemStatus.PrivilegeError: errorCause = ErrorType.InstrPageFault; break;
                    default: fetchError = false; break;
                }

                instr = fetchError ? 0 : instrResp.Instruction;
            }
            else
            {
                errorCause = ErrorType.InstrUnaligned;
                instr = 0;
            }

            UInt64 instrSize = 4;
            if (UseC)
            {
                instr = ExpandCompressed(instr);
                instrSize = GetSize(instr);
            }

            var npc = curr_pc + instrSize;

            if ((!fetchError) && (E(instr, 1, 0) != 3))
            {
                tval = instr;
                instr = 0;
            }

            // Decode instruction
            var opcode = (Opcode)((instr >> 2) & 0x1F);
            var rd = E(instr, 11, 7);
            var rs1 = E(instr, 19, 15);
            var rs2 = E(instr, 24, 20);
            var f3 = E(instr, 14, 12);
            var f7 = E(instr, 31, 25);

            var immI = SignExtend(E(instr, 31, 20), 11);
            var immS = SignExtend(
                (E(instr, 31, 25) << 5) |
                E(instr, 11, 7),
                11
            );
            var immB = SignExtend(
                E(instr, 31, 31) << 12 |
                E(instr, 7, 7) << 11 |
                E(instr, 30, 25) << 5 |
                E(instr, 11, 8) << 1,
                12
            );
            var immU = E(instr, 31, 12) << 12;
            var immJ = SignExtend(
                E(instr, 31, 31) << 20 |
                E(instr, 19, 12) << 12 |
                E(instr, 20, 20) << 11 |
                E(instr, 30, 21) << 1,
                20
            );

            var vrs1 = regs[rs1];
            var vrs2 = regs[rs2];

            if (rs1 == 0) vrs1 = 0;
            if (rs2 == 0) vrs2 = 0;

            var isR = (opcode == Opcode.add) || (opcode == Opcode.addw);
            var isI = (opcode == Opcode.addi) || (opcode == Opcode.addiw) || (opcode == Opcode.ld) || (opcode == Opcode.sys) || (opcode == Opcode.jalr);
            var isS = (opcode == Opcode.sd);
            var isB = (opcode == Opcode.beq);
            var isU = (opcode == Opcode.lui) || (opcode == Opcode.auipc);
            var isJ = (opcode == Opcode.jal);

            var imm =
                isI ? immI :
                isS ? immS :
                isB ? immB :
                isU ? immU :
                isJ ? immJ :
                0;

            var isW = (opcode == Opcode.addw) || (opcode == Opcode.addiw);

            var op2 = isR ? vrs2 : imm;
            var aluf7 = isR ? f7 : 0;
            var aluf3 = isR || isI ? f3 : 0;

            // Execute
            UInt64 result = AluOp(vrs1, op2, aluf3, aluf7, (UInt64)(isW ? 0x1F : 0x3F));

            var f7_w = (E(f7, 4, 0) == 0) && (E(f7, 6, 6) == 0);
            var f7_n = E(f7, 5, 5) != 0;

            var add_illegal = ((f3 == 0) || (f3 == 5)) ? false : !f7_n;
            var addi_illegal = (f3 == 1) ? f7_n || !f7_w :
                               (f3 == 5) ? !f7_w :
                               false;

            var pc_imm = opcode == Opcode.jalr ? result : imm;
            var calc_pc = curr_pc + pc_imm;

            UInt64 mem_addr = 0;
            UInt64 mem_rdata = 0;
            UInt64 mem_wdata = 0;
            int mem_rmask = 0;
            int mem_wmask = 0;

            var result_signextended = SignExtend(result, 31);

            switch (opcode)
            {
                case Opcode.add:
                    if (!f7_w || add_illegal)
                        goto default;
                    if (rd != 0) regs[rd] = result;
                    if (!IncludeRVFI) { pc = npc; return; }
                    break;

                case Opcode.addi:
                    if (addi_illegal)
                        goto default;
                    if (rd != 0) regs[rd] = result;
                    if (!IncludeRVFI) { pc = npc; return; }
                    break;

                case Opcode.addw:
                    if (!f7_w || add_illegal || !is64)
                        goto default;
                    result = result_signextended;
                    if (rd != 0) regs[rd] = result;
                    if (!IncludeRVFI) { pc = npc; return; }
                    break;

                case Opcode.addiw:
                    if (addi_illegal || !is64)
                        goto default;
                    result = result_signextended;
                    if (rd != 0) regs[rd] = result;
                    if (!IncludeRVFI) { pc = npc; return; }
                    break;

                case Opcode.lui:
                    if (rd != 0) regs[rd] = imm;
                    if (!IncludeRVFI) { pc = npc; return; }
                    break;
                case Opcode.auipc:
                    result = calc_pc;
                    if (rd != 0) regs[rd] = result;
                    if (!IncludeRVFI) { pc = npc; return; }
                    break;

                case Opcode.jalr:
                    if (f3 != 0) goto default;
                    goto case Opcode.jal;
                case Opcode.jal:
                    {
                        result = npc;
                        if (rd != 0) regs[rd] = result;
                        npc = calc_pc;
                        if (!IncludeRVFI) { pc = npc; return; }
                        break;
                    }

                case Opcode.beq:
                    {
                        if (Cond(f3, vrs1, vrs2))
                            npc = calc_pc;
                        if (!IncludeRVFI) { pc = npc; return; }
                        break;
                    }

                case Opcode.ld:
                    {
                        MemWidth w = MemWidth.B;
                        UInt64 mask = 0;
                        int msb = 0;
                        UInt64 addrMask = 0;
                        bool fail = false;

                        var _f3 = (F3_Load)f3;

                        switch (_f3)
                        {
                            case F3_Load.LBU:
                            case F3_Load.LB: msb = 7; mask = 0xFF; w = MemWidth.B; break;
                            case F3_Load.LHU:
                            case F3_Load.LH: addrMask = 1; msb = 15; mask = 0xFFFF; w = MemWidth.H; break;
                            case F3_Load.LWU:
                                fail = !is64;
                                goto case F3_Load.LW;
                            case F3_Load.LW: addrMask = 3; msb = 31; mask = 0xFFFF_FFFF; w = MemWidth.W; break;
                            case F3_Load.LD: addrMask = 7; msb = 63; mask = 0xFFFF_FFFF_FFFF_FFFF; w = MemWidth.D; fail = !is64; break;
                            default: fail = true; break;
                        }

                        var signed = (_f3 == F3_Load.LB) || (_f3 == F3_Load.LH) || (_f3 == F3_Load.LW);

                        if (fail)
                            goto default;
                        else if ((result & addrMask) != 0)
                        {
                            errorCause = ErrorType.LoadUnaligned;
                            goto default;
                        }

                        var resp = DMem.Request(new MemRequest
                        {
                            Address = result & addrSpaceMask,
                            IsWrite = false,
                            Width = w,
                            WriteValue = 0
                        });

                        if (resp.Status != MemStatus.Ok)
                        {
                            switch (resp.Status)
                            {
                                case MemStatus.BusError:
                                case MemStatus.DeviceError: errorCause = ErrorType.LoadAccess; break;
                                case MemStatus.PrivilegeError: errorCause = ErrorType.LoadPageFault; break;
                            }

                            goto default;
                        }

                        mem_addr = result & addrSpaceMask;
                        mem_rdata = resp.ReadValue;
                        mem_rmask = (int)((1UL << (int)addrMask) - 1);

                        result = resp.ReadValue;

                        if (signed)
                        {
                            var signMask = SignExtend(result >> msb, 0);
                            result = (result & mask) | (signMask & ~mask);
                        }
                        if (rd != 0) regs[rd] = result;
                        if (!IncludeRVFI) { pc = npc; return; }

                        break;
                    }

                case Opcode.sd:
                    {
                        MemWidth w = MemWidth.B;
                        bool fail = false;
                        UInt64 addrMask = 0;

                        switch ((F3_Store)f3)
                        {
                            case F3_Store.SB: w = MemWidth.B; break;
                            case F3_Store.SH: addrMask = 1; w = MemWidth.H; break;
                            case F3_Store.SW: addrMask = 3; w = MemWidth.W; break;
                            case F3_Store.SD: addrMask = 7; w = MemWidth.D; fail = !is64; break;
                            default: fail = true; break;
                        }

                        if (fail)
                            goto default;
                        else if ((result & addrMask) != 0)
                        {
                            errorCause = ErrorType.LoadUnaligned;
                            goto default;
                        }

                        var resp = DMem.Request(new MemRequest
                        {
                            Address = result & addrSpaceMask,
                            IsWrite = true,
                            Width = w,
                            WriteValue = vrs2
                        });

                        mem_addr = result & addrSpaceMask;
                        mem_wdata = vrs2;
                        mem_wmask = (int)((1UL << (int)addrMask) - 1);

                        if (resp.Status != MemStatus.Ok)
                        {
                            switch (resp.Status)
                            {
                                case MemStatus.BusError:
                                case MemStatus.DeviceError: errorCause = ErrorType.StoreAccess; break;
                                case MemStatus.PrivilegeError: errorCause = ErrorType.StorePageFault; break;
                            }

                            goto default;
                        }

                        rd = 0;
                        if (!IncludeRVFI) { pc = npc; return; }

                        break;
                    }

                case Opcode.fence:
                    {
                        // NOP
                        rd = 0;
                        if (!IncludeRVFI) { pc = npc; return; }

                        break;
                    }

                case Opcode.sys:
                    {
                        if ((F3_Sys)f3 == F3_Sys.ECall)
                        {
                            if ((rd == 0) && (rs1 == 0) && (E(instr, 31, 21) == 0))
                            {
                                int CurrentPP = (int)curr_priv;
                                errorCause =
                                    (E(instr, 20, 20) == 0) ? (ErrorType.ECallU + CurrentPP) :
                                    ErrorType.BreakPoint;
                            }
                            goto default;
                        }
                        else if (f3 == 4)
                            goto default;
                        else
                        {
                            // CSR operations
                            var _f3 = (F3_Sys)f3;

                            var writeValue =
                                ((_f3 == F3_Sys.CSRRCI) || (_f3 == F3_Sys.CSRRSI) || (_f3 == F3_Sys.CSRRWI)) ? rs1 :
                                vrs1;

                            var doWrite =
                                ((_f3 == F3_Sys.CSRRCI) || (_f3 == F3_Sys.CSRRSI) || (_f3 == F3_Sys.CSRRC) || (_f3 == F3_Sys.CSRRS)) ? (rs1 != 0) :
                                true;

                            UInt64 cMask = 0xFFFF_FFFF_FFFF_FFFF;
                            UInt64 sMask = writeValue;

                            switch (_f3)
                            {
                                case F3_Sys.CSRRS:
                                case F3_Sys.CSRRSI:
                                    cMask = 0;
                                    break;
                                case F3_Sys.CSRRC:
                                case F3_Sys.CSRRCI:
                                    cMask = sMask;
                                    sMask = 0;
                                    break;
                            }

                            var csr = imm;

                            var res = WriteRegister(csr, cMask, sMask, doWrite, rd != 0, is64);

                            if (res.Ok)
                            {
                                result = res.OldValue;
                                if (rd != 0) regs[rd] = result;
                            }
                            else
                            {
                                errorCause = ErrorType.IllegalInstruction;
                                goto default;
                            }
                        }
                        if (!IncludeRVFI) { pc = npc; return; }

                        break;
                    }

                default:
                    {
                        var curr_mstatus = mstatus;

                        mstatus = (curr_mstatus & 0xFFFF_FFFF_FFFF_E777) |
                            (((UInt64)curr_priv) << 11) |
                            (E(curr_mstatus, 3, 3) << 7);
                        mcause = (UInt64)errorCause;
                        mepc = curr_pc;
                        mtval = tval;

                        privilege = PrivilegeLevel.M;
                        npc = CalcVector(mtvec, wasIntr, errorCause);

                        wasTrap = true;

                        rd = 0;
                        break;
                    }
            }

            pc = npc;

            if (IncludeRVFI)
                RVFI.WriteValue(new RVFIPacket
                {
                    valid = true,
                    order = RVFI_Order++,
                    insn = instr,
                    trap = wasTrap,
                    halt = false,
                    intr = wasTrap && wasIntr,

                    rs1_addr = (int)rs1,
                    rs2_addr = (int)rs2,
                    rs1_rdata = vrs1,
                    rs2_rdata = vrs2,

                    rd_addr = (int)rd,
                    rd_rdata = result,

                    pc_rdata = curr_pc,
                    pc_wdata = npc,

                    mem_addr = mem_addr,
                    mem_rdata = mem_rdata,
                    mem_rmask = mem_rmask,
                    mem_wdata = mem_wdata,
                    mem_wmask = mem_wmask
                });
        }

        private ulong CalcVector(ulong mtvec, bool isInterrupt, ErrorType errorCause)
        {
            var offset = isInterrupt ? 0 : (((UInt64)errorCause) << 2);

            var vecBase = (mtval >> 2) << 2;
            var type = E(mtval, 1, 0);

            switch (type)
            {
                default:
                case 0: return vecBase;
                case 1: return vecBase + offset;
            }
        }

        [ResetValue(0)]
        UInt64 RVFI_Order;

        public RiscV()
        {
            regs = new UInt64[32];
        }
    }

    public struct RVFIPacket
    {
        public bool valid;
        public UInt64 order;
        public UInt32 insn;
        public bool trap;
        public bool halt;
        public bool intr;

        [BitWidth(5)]
        public int rs1_addr;
        [BitWidth(5)]
        public int rs2_addr;
        public UInt64 rs1_rdata;
        public UInt64 rs2_rdata;

        [BitWidth(5)]
        public int rd_addr;
        public UInt64 rd_rdata;

        public UInt64 pc_rdata;
        public UInt64 pc_wdata;

        public UInt64 mem_addr;
        [BitWidth(64 / 8)]
        public int mem_rmask;
        [BitWidth(64 / 8)]
        public int mem_wmask;
        public UInt64 mem_rdata;
        public UInt64 mem_wdata;
    }

    public struct MemRequest
    {
        public UInt64 Address;
        public MemWidth Width;
        public UInt64 WriteValue;
        public bool IsWrite;
    }

    public struct MemResponse
    {
        public UInt64 ReadValue;
        public MemStatus Status;
    }

    public struct InstrResponse
    {
        public UInt32 Instruction;
        public MemStatus Status;
    }

    [BitWidth(2)]
    public enum MemStatus
    {
        Ok = 0,
        DeviceError = 1,
        BusError = 2,
        PrivilegeError = 3
    }

    [BitWidth(2)]
    public enum MemWidth
    {
        B,
        H,
        W,
        D
    }

    public enum PrivilegeLevel
    {
        U = 0,
        S = 1,
        M = 3
    }

    public struct CSRResult
    {
        public bool Ok;
        public UInt64 OldValue;
    }

    [BitWidth(4)]
    public enum ErrorType
    {
        InstrUnaligned = 0,
        InstrAccess = 1,
        IllegalInstruction = 2,
        BreakPoint = 3,
        LoadUnaligned = 4,
        LoadAccess = 5,
        StoreUnaligned = 6,
        StoreAccess = 7,
        ECallU = 8,
        ECallS = 9,
        ECallM = 11,
        InstrPageFault = 12,
        LoadPageFault = 13,
        StorePageFault = 15
    }

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

        sys = 0x1C,


        // RV64I
        addiw = 0x06,
        addw = 0x0E,

        // RV32M
        mul = 0x0c,
        // RV64M
        mulw = 0x0E,

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
        LD = 3,
    }

    public enum F3_Store : UInt32
    {
        SB = 0,
        SH = 1,
        SW = 2,
        SD = 3,
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

    public enum F3_Sys : UInt32
    {
        ECall = 0,

        CSRRW = 1,
        CSRRS = 2,
        CSRRC = 3,

        CSRRWI = 5,
        CSRRSI = 6,
        CSRRCI = 7,
    }
}