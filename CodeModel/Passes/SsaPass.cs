using System;
using System.Collections.Generic;
using System.Linq;

namespace CilLogic.CodeModel.Passes
{
    public class SsaPass : CodePass
    {
        public override void Pass(Method method)
        {
            if (method.Locals == 0) return;

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
                        b.Replace(instr, new Opcode(instr.Result, Op.Mov, new ValueOperand(locals[loc])));
                    }
                    else if(instr.Op == Op.StLoc)
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
            foreach(var locs in entryLocals[method.Entry])
                method.Entry.Prepend(new Opcode(locs, Op.Mov, new UndefOperand()));

            // Add phi nodes
            var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.Last().Operands.OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());
            foreach(var block in method.Blocks.Where(b => b != method.Entry))
            {
                var prevBlocks = nextBlocks.Where(kvp => kvp.Value.Contains(block)).Select(x => x.Key).ToList();

                for(int i=0; i<method.Locals; i++)
                    block.Prepend(new Opcode(entryLocals[block][i], Op.Phi, prevBlocks.Select(pb => new PhiOperand(pb, new ValueOperand(exitLocals[pb][i]))).ToArray()));
            }
        }
    }
}