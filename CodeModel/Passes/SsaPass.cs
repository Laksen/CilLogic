using System;
using System.Collections.Generic;
using System.Linq;
using CilLogic.Utilities;
using Mono.Cecil;

namespace CilLogic.CodeModel.Passes
{
    public class PreSsaPass : CodePass
    {
        public override void Pass(Method method)
        {
            foreach (var op in method.AllInstructions())
            {
                switch (op.Op)
                {
                    case Op.InitObj:
                        {
                            var locRef = (op[0] as LocOperand).Location;
                            var typ = (op[1] as TypeOperand);

                            var v = method.GetValue();
                            op.Block.InsertBefore(new Opcode(v, Op.Slice, typ.OperandType.GetWidth(), 0, 0, 0), op);
                            op.Block.Replace(op, new Opcode(0, Op.StLoc, locRef, new ValueOperand(v, typ.OperandType)));

                            break;
                        }
                    case Op.LdFld:
                        {
                            if (op[0] is LocOperand locRef)
                            {
                                var fref = (op[1] as FieldOperand).Field;

                                var tw = fref.Resolve().GetInfo(fref.DeclaringType.Resolve(), method);

                                var ldLoc = new Opcode(method.GetValue(), Op.LdLoc, locRef.Location, new TypeOperand(method.LocalTypes[locRef.Location], method));
                                var extract = new Opcode(op.Result, Op.Slice, new ValueOperand(ldLoc.Result, ldLoc.GetResultType(method)), tw.Msb, tw.Lsb, 0, 0);

                                op.Block.InsertBefore(ldLoc, op);
                                op.Block.Replace(op, extract);
                            }
                            else if (op[0] is ValueOperand vo)
                            {
                                var fref = (op[1] as FieldOperand).Field;

                                var tw = fref.Resolve().GetInfo(fref.DeclaringType.Resolve(), method);

                                var extract = new Opcode(op.Result, Op.Slice, vo, tw.Msb, tw.Lsb, 0, 0);

                                op.Block.Replace(op, extract);
                            }

                            break;
                        }
                    case Op.StFld:
                        {
                            if (op[0] is LocOperand locRef)
                            {
                                var fref = (op[1] as FieldOperand).Field;
                                var value = op[2];

                                var tw = fref.Resolve().GetInfo(fref.DeclaringType.Resolve(), method);

                                var load = new Opcode(method.GetValue(), Op.LdLoc, locRef.Location);
                                var insert = new Opcode(method.GetValue(), Op.Insert, new ValueOperand(load.Result, new CecilType<TypeDefinition>(fref.DeclaringType.Resolve(method.GenericParams), method)), tw.Msb, tw.Lsb, value);
                                var store = new Opcode(0, Op.StLoc, locRef.Location, new ValueOperand(insert.Result, new CecilType<TypeDefinition>(fref.DeclaringType.Resolve(method.GenericParams), method)));

                                op.Block.InsertBefore(load, op);
                                op.Block.InsertBefore(insert, op);
                                op.Block.Replace(op, store);
                            }
                            break;
                        }
                }
            }
        }
    }

    public class SsaPass : CodePass
    {
        public override void Pass(Method method)
        {
            if (method.Locals == 0) return;

            DoPass<PreSsaPass>(method, ">");

            method.IsSSA = true;

            // Create locals
            var entryLocals = new Dictionary<BasicBlock, int[]>();
            var exitLocals = new Dictionary<BasicBlock, int[]>();

            foreach (var b in method.Blocks)
            {
                entryLocals[b] = Enumerable.Range(0, method.Locals).Select(i => method.GetValue()).ToArray();
                var locals = entryLocals[b].ToArray();

                foreach (var instr in b.Instructions.ToList())
                {
                    if (instr.Op == Op.LdLoc)
                    {
                        var loc = (instr[0] as ConstOperand).Value;
                        b.Replace(instr, new Opcode(instr.Result, Op.Mov, new ValueOperand(locals[loc], new CecilType<TypeDefinition>(method.LocalTypes[loc], method))));
                    }
                    else if (instr.Op == Op.StLoc)
                    {
                        var loc = (instr[0] as ConstOperand).Value;
                        var newLoc = method.GetValue();
                        locals[loc] = newLoc;

                        var newOp = new Opcode(newLoc, Op.Mov, instr[1]);
                        b.Replace(instr, newOp);
                    }
                }

                exitLocals[b] = locals;
            }

            // Add initialization
            for (int i = 0; i < method.Locals; i++)
            {
                var locs = entryLocals[method.Entry][i];
                //foreach (var locs in entryLocals[method.Entry])
                method.Entry.Prepend(new Opcode(locs, Op.Mov, new ConstOperand(0, method.LocalTypes[i], method)));
            }

            // Add phi nodes
            var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());
            foreach (var block in method.Blocks.Where(b => b != method.Entry))
            {
                var prevBlocks = nextBlocks.Where(kvp => kvp.Value.Contains(block)).Select(x => x.Key).ToList();

                for (int i = 0; i < method.Locals; i++)
                    block.Prepend(new Opcode(entryLocals[block][i], Op.Phi, prevBlocks.Select(pb => new PhiOperand(pb, new ValueOperand(exitLocals[pb][i], new CecilType<TypeDefinition>(method.LocalTypes[i], method)))).ToArray()));
            }
        }
    }
}