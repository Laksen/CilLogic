using System;
using System.Collections.Generic;
using System.Linq;

namespace CilLogic.CodeModel.Passes
{
    public class PassDeadCode : CodePass
    {
        private void RemoveUnused(Method method)
        {
            bool wasSuccess = false;

            // TODO: Use decrementing operations instead of calculating everything again and again
            do
            {
                wasSuccess = false;

                // Simple instr removal
                var instrs = method.Blocks.SelectMany(o => o.Instructions).ToHashSet();

                var instrProvides = instrs.ToDictionary(i => i, i => i.Result);
                var instrProviders = instrProvides.Where(kvp => kvp.Value != 0).ToDictionary(i => i.Value, i => i.Key);
                var usages = instrs.ToDictionary(i => i, i => i.Operands.OfType<ValueOperand>().Select(v => v.Value).Concat(
                    i.Operands.OfType<PhiOperand>().Select(v => v.Value).OfType<ValueOperand>().Select(v => v.Value)).Distinct().ToList());
                var instrUsers = instrs.ToDictionary(i => i, i => usages.Where(u => u.Value.Contains(i.Result)).Select(u => u.Key).ToList());

                var useCount = instrs.ToDictionary(i => i, i => (i.HasSideEffects() ? 1 : 0) + instrUsers[i].Count());

                var toRemove = useCount.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).ToList();

                foreach (var instr in toRemove)
                {
                    wasSuccess = true;
                    instr.Block.Instructions.Remove(instr);
                }
            }
            while (wasSuccess);
        }

        private void RemoveStaleBranches(Method method)
        {
            foreach (var bb in method.Blocks)
            {
                var final = bb.Instructions.FirstOrDefault(p => p.IsTerminating());
                var index = bb.Instructions.IndexOf(final);

                bb.Instructions.RemoveRange(index + 1, bb.Instructions.Count - index - 1);
            }
        }

        private void RemoveDeadBlocks(Method method)
        {
            bool wasSuccess = true;
            while (wasSuccess)
            {
                var targets = method.Blocks.SelectMany(b => b.Instructions.SelectMany(i => i.Operands.OfType<BlockOperand>().Select(bo => bo.Block))).ToHashSet();

                wasSuccess = method.Blocks.RemoveAll(b => !(b == method.Entry || targets.Contains(b))) > 0;
            }
        }

        private void RemoveDeadJumps(Method method)
        {
            var jumpBlocks = method.Blocks.Where(b => (b.Instructions.Count == 1) && (b.Instructions[0].Op == Op.Br)).ToList();

            var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.Last().Operands.OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());
            foreach (var blk in jumpBlocks)
            {
                var prevBlocks = nextBlocks.Where(kvp => kvp.Value.Contains(blk)).Select(x => x.Key).ToList();

                if (prevBlocks.Count > 1) continue; // TODO: Fix up phi nodes

                method.ReplaceBlockOperand(blk, blk.Instructions[0][0] as BlockOperand);
            }
        }

        public override void Pass(Method method)
        {
            RemoveUnused(method);
            RemoveStaleBranches(method);
            RemoveDeadJumps(method);
            RemoveDeadBlocks(method);
        }
    }
}