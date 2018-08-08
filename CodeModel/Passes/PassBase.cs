using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CilLogic.Utilities;

namespace CilLogic.CodeModel.Passes
{
    public abstract class CodePass
    {
        private const bool DebugPasses = false;

        public abstract void Pass(Method method);

        public static Dictionary<string, TimeSpan> PassTime = new Dictionary<string, TimeSpan>();

        public static void DoPass<T>(Method m, string tag = "") where T : CodePass, new()
        {
            string prePass;
            if (DebugPasses)
                prePass = m.ToString();

            Stopwatch s = Stopwatch.StartNew();
            new T().Pass(m);
            s.Stop();

            var name = typeof(T).Name;

            if (!PassTime.ContainsKey(name))
                PassTime[name] = new TimeSpan(0);

            PassTime[name] += s.Elapsed;

            if (DebugPasses)
            {
                var instrs = m.AllInstructions();

                int GetValue(Operand oper)
                {
                    if (oper is ValueOperand vo)
                        return vo.Value;
                    else if (oper is PhiOperand po)
                        return GetValue(po.Value);
                    return 0;
                }

                try
                {
                    var nexts = m.Blocks.ToDictionary(b => b, b => b.NextBlocks());

                    foreach (var blk in m.Blocks)
                    {
                        var prevBlocks = nexts.Where(kvp => kvp.Value.Contains(blk)).Select(x => x.Key).ToList();

                        foreach (var instr in blk.Instructions.Where(x => x.Op == Op.Phi))
                        {
                            if (instr.Operands.OfType<PhiOperand>().Any(p => !prevBlocks.Contains(p.Block)))
                                throw new Exception("Pass made phi operand source disappear");
                        }
                    }

                    if (instrs.Where(x => x.Result != 0).GroupBy(r => r.Result).Any(i => i.Count() > 1))
                        throw new Exception($"Pass caused instruction to be duplicated: {typeof(T).Name}");

                    var produced = instrs.Where(x => x.Result != 0).Select(x => x.Result).ToHashSet();
                    var used = instrs.SelectMany(x => x.Operands).Select(GetValue).Where(x => x != 0).ToHashSet();

                    if (used.Except(produced).Any())
                    {
                        var removed = string.Join(", ", used.Except(produced));
                        throw new Exception($"Pass caused instruction to be removed: {typeof(T).Name}. Removed: {removed}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    File.WriteAllText(@"C:\Users\jepjoh2\Desktop\Pre.txt", prePass);
                    File.WriteAllText(@"C:\Users\jepjoh2\Desktop\Post.txt", m.ToString());
                    throw ex;
                }
            }
        }

        public static void Process(Method m, bool ssa = true)
        {
            DoPass<PassDeadCode>(m);
            CodePass.DoPass<BreakSwitch>(m);
            DoPass<PassPeephole>(m);
            DoPass<PassDeadCode>(m);

            DoPass<PassDeadCode>(m);

            DoPass<InlinePass>(m);

            DoPass<PassDeadCode>(m);
            DoPass<ReuseDuplicates>(m);

            if (ssa) DoPass<SsaPass>(m);

            for (int i = 0; i < 24; i++)
            {
                DoPass<PassPeephole>(m);
                DoPass<PassDeadCode>(m);
                DoPass<ReuseDuplicates>(m);
            }

            DoPass<PassDeadCode>(m);
        }
    }
}