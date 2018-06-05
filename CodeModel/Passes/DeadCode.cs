using System;
using System.Collections.Generic;
using System.Linq;
using CilLogic.Utilities;

namespace CilLogic.CodeModel.Passes
{
    public class PassDeadValues : CodePass
    {
        public override void Pass(Method method)
        {
            // Simple instr removal
            var instrs = method.Blocks.SelectMany(o => o.Instructions).ToHashSet();

            var instrProvides = instrs.ToDictionary(i => i, i => i.Result);

            var instrProviders = instrProvides.Where(kvp => kvp.Value != 0).ToDictionary(i => i.Value, i => i.Key);
            var usages = instrs.ToDictionary(i => i, i => i.Operands.OfType<ValueOperand>().Select(v => v.Value).Concat(
                i.Operands.OfType<PhiOperand>().Select(v => v.Value).OfType<ValueOperand>().Select(v => v.Value)).Distinct().ToList());

            var instrUsers = instrs.ToDictionary(i => i, i => new HashSet<Opcode>());
            foreach (var usage in usages)
                foreach (var value in usage.Value)
                    if (instrProviders.ContainsKey(value))
                        instrUsers[instrProviders[value]].Add(usage.Key);

            var useCount = instrs.ToDictionary(i => i, i => (i.HasSideEffects() ? 1 : 0) + instrUsers[i].Count);

            while (true)
            {
                var toRemove = useCount.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).ToList();

                if (toRemove.Count == 0) break;

                foreach (var instr in toRemove)
                {
                    instr.Block.Instructions.Remove(instr);

                    if (instr.Result != 0) instrProviders.Remove(instr.Result);

                    foreach (var oper in instr.Operands)
                    {
                        var op = 0;
                        if ((oper is PhiOperand po) && (po.Value is ValueOperand vo))
                            op = vo.Value;
                        else if (oper is ValueOperand vo2)
                            op = vo2.Value;

                        if (op != 0)
                        {
                            if (instrProviders.ContainsKey(op))
                            {
                                var provider = instrProviders[op];
                                useCount[provider]--;
                            }
                        }
                    }

                    useCount.Remove(instr);
                    instrUsers.Remove(instr);
                }
            }
        }
    }

    public class RemoveStaleBranches : CodePass
    {
        public override void Pass(Method method)
        {
            foreach (var bb in method.Blocks)
            {
                var final = bb.Instructions.FirstOrDefault(p => p.IsTerminating());
                var index = bb.Instructions.IndexOf(final);

                bb.Instructions.RemoveRange(index + 1, bb.Instructions.Count - index - 1);
            }
        }
    }

    public class PassDeadCode : CodePass
    {
        private void RemoveDeadBlocks(Method method)
        {
            /*bool wasSuccess = true;
            while (wasSuccess)
            {
                var targets = method.Blocks.SelectMany(b => b.Instructions.SelectMany(i => i.Operands.OfType<BlockOperand>().Select(bo => bo.Block))).Concat(new[] { method.Entry }).ToHashSet();

                wasSuccess = method.Blocks.RemoveAll(b => !targets.Contains(b)) > 0;
            }*/

            HashSet<BasicBlock> visited = new HashSet<BasicBlock>();
            HashSet<BasicBlock> unused = new HashSet<BasicBlock>(method.Blocks);

            Queue<BasicBlock> toVisit = new Queue<BasicBlock>(new[] { method.Entry });

            Action<BasicBlock> doVisit = n =>
            {
                if (!visited.Contains(n))
                {
                    visited.Add(n);
                    unused.Remove(n);

                    foreach (var x in n.Instructions.SelectMany(i => i.Operands))
                        if (x is BlockOperand bo)
                            toVisit.Enqueue(bo.Block);
                }
            };

            while (toVisit.Count > 0)
            {
                var n = toVisit.Dequeue();
                doVisit(n);
            }

            if (unused.Count > 0)
            {
                method.Blocks.RemoveAll(unused.Contains);
                method.AllInstructions().ForEach(ins => ins.Operands.RemoveAll(oper => (oper is PhiOperand po) && unused.Contains(po.Block)));
            }
        }

        public class RemoveDeadJumps : CodePass
        {
            public override void Pass(Method method)
            {
                bool wasOk = true;
                while (wasOk)
                {
                    wasOk = false;
                    var jumpBlocks = method.Blocks.Where(b => (b.Instructions.Count == 1) && (b.Instructions[0].Op == Op.Br) && (b != method.Entry)).ToList();

                    var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());
                    foreach (var blk in jumpBlocks)
                    {
                        var prevBlocks = nextBlocks.Where(kvp => kvp.Value.Contains(blk)).Select(x => x.Key).ToList();

                        if (prevBlocks.Count != 1) continue;
                        if (nextBlocks[blk].Count != 1) continue;

                        var next = nextBlocks[blk].Single();

                        var nextPrevs = nextBlocks.Where(kvp => kvp.Value.Contains(next)).Select(x => x.Key).ToList();
                        if (nextPrevs.Contains(prevBlocks.Single())) continue;

                        if (next.Instructions.Any(i => (i.Op == Op.Phi) && ContainsPhiSource(i, prevBlocks[0]))) continue;

                        method.ReplaceBlockOperand(blk, new BlockOperand(next));

                        wasOk = true;
                        break;
                    }
                }
            }

            private bool ContainsPhiSource(Opcode i, BasicBlock basicBlock)
            {
                return true;
                //return i.Operands.OfType<PhiOperand>().Any(x => x.Block == basicBlock);
            }
        }

        private void SpliceBlocks(Method method)
        {
            bool wasOK;
            do
            {
                wasOK = false;
                var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());

                foreach (var block in method.Blocks)
                {
                    var prevBlocks = nextBlocks.Where(kvp => kvp.Value.Contains(block)).Select(x => x.Key).ToList();

                    if ((prevBlocks.Count == 1) &&
                        (nextBlocks[prevBlocks[0]].Count == 1) &&
                        !block.Instructions.Any(i => i.Op == Op.Phi))
                    {
                        var pb = prevBlocks[0];

                        method.ReplaceBlockOperand(pb, new BlockOperand(block));

                        foreach (var instr in pb.Instructions.Reverse<Opcode>().Skip(1))
                            block.Prepend(instr);

                        method.Blocks.Remove(pb);
                        if (method.Entry == pb)
                            method.Entry = block;

                        wasOK = true;
                        break;
                    }
                }
            }
            while (wasOK);
        }

        public class EliminateJumpThrough : CodePass
        {
            public override void Pass(Method method)
            {
                while (true)
                {
                    var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());
                    var thruBlocks = method.Blocks.Where(x => (x.Instructions.Count == 1) && (x.Instructions.Last().Op == Op.Br)).ToHashSet();

                    var blk = method.Blocks.Except(thruBlocks).FirstOrDefault(f =>
                    {
                        var nb = nextBlocks[f];
                        if ((nb.Count == 2) && (nb.Count(thruBlocks.Contains) == 1))
                        {
                            var thru = nb.FirstOrDefault(thruBlocks.Contains);
                            var next = nextBlocks[thru].Single();

                            if (nb.Contains(next)) return false;

                            return true;
                        }

                        return false;
                    });

                    if (blk == null) break;

                    {
                        var nb = nextBlocks[blk];
                        var thru = nb.FirstOrDefault(thruBlocks.Contains);
                        var next = nextBlocks[thru].Single();

                        foreach (var ins in blk.Instructions)
                            for (int i = 0; i < ins.Operands.Count; i++)
                            {
                                var oper = ins[i];
                                if ((oper is BlockOperand bo) && (bo.Block == thru))
                                    ins.Operands[i] = new BlockOperand(next);
                            }

                        foreach (var phi in next.Instructions.Where(i => i.Op == Op.Phi))
                        {
                            var oldOp = phi.Operands.OfType<PhiOperand>().Where(po => po.Block == thru).Select(po => po.Value).FirstOrDefault();
                            if (oldOp != null)
                                phi.Operands.Add(new PhiOperand(blk, oldOp));
                        }
                    }
                }
            }
        }

        public class CodeHoist : CodePass
        {
            public override void Pass(Method method)
            {
                while (true)
                {
                    var nextBlocks = method.Blocks.ToDictionary(b => b, b => b.Instructions.SelectMany(o => o.Operands).OfType<BlockOperand>().Select(bo => bo.Block).ToHashSet());
                    var thruBlocks = method.Blocks.Where(x => (nextBlocks[x].Count == 1) && !x.Instructions.Where(o => o.Op != Op.Br).Any(y => y.HasSideEffects() || y.Op == Op.Phi)).ToHashSet();

                    var blk = method.Blocks.Except(thruBlocks).FirstOrDefault(f =>
                    {
                        var nb = nextBlocks[f];
                        if (nb.Any(thruBlocks.Contains))
                        {
                            var thru = nb.Where(thruBlocks.Contains).ToList();

                            if (thru.All(x => x.Instructions.Count <= 1)) return false;
                            if (f.Instructions.Where(x => !x.IsCondJump || x.Op == Op.Br).Any(x => x.HasSideEffects())) return false;

                            return true;
                        }

                        return false;
                    });

                    if (blk == null) break;

                    var toMigrate = nextBlocks[blk].Where(thruBlocks.Contains).ToList();

                    var point = blk.Instructions.First();

                    foreach (var rins in toMigrate.SelectMany(x => x.Instructions.Where(o => o.Op != Op.Br)).ToList())
                    {
                        blk.InsertBefore(rins, point);
                        toMigrate.ForEach(f => f.Instructions.Remove(rins));
                    }
                }
            }
        }

        public override void Pass(Method method)
        {
            CodePass.DoPass<RemoveStaleBranches>(method, ">");
            CodePass.DoPass<PassDeadValues>(method, ">");
            CodePass.DoPass<CodeHoist>(method, ">");
            CodePass.DoPass<RemoveDeadJumps>(method, ">");
            RemoveDeadBlocks(method);
            SpliceBlocks(method);
            CodePass.DoPass<EliminateJumpThrough>(method, ">");
            RemoveDeadBlocks(method);
        }
    }
}