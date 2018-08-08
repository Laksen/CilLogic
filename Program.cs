using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CilLogic.CodeModel;
using CilLogic.CodeModel.Passes;
using CilLogic.Scheduling;
using CilLogic.Utilities;

using Mono.Cecil;

namespace CilLogic
{
    public class HoistAll : CodePass
    {
        public override void Pass(Method method)
        {
            var oldInstr = method.AllInstructions().Where(i => !i.HasSideEffects() && i.Op != Op.Phi).ToList();
            foreach (var instr in oldInstr)
            {
                if (instr.Block == method.Entry) continue;

                instr.Block.Instructions.Remove(instr);
                method.Entry.Prepend(instr);
            }
        }
    }

    public class BreakSwitch : CodePass
    {
        public override void Pass(Method method)
        {
            void SplitJump(BasicBlock jumpBlock, Operand value, Operand[] targets)
            {
                var realTargest = targets.Count(o => o != null);

                if (realTargest == 1)
                    jumpBlock.Append(new Opcode(0, Op.Br, targets.First(t => t != null)));
                else if (targets.Length == 1)
                    jumpBlock.Append(new Opcode(0, Op.Br, targets[0]));
                else if (targets.Length == 2)
                    jumpBlock.Append(new Opcode(0, Op.BrCond, value, targets[1], targets[0]));
                else
                {
                    var bits = (int)Math.Ceiling(Math.Log(targets.Length) / Math.Log(2));

                    var t0 = targets.Take(1 << (bits - 1)).ToArray();
                    var t1 = targets.Skip(1 << (bits - 1)).ToArray();

                    var rest = jumpBlock.Prepend(new Opcode(method.GetValue(), Op.Slice, value, bits - 2, 0, 0, 0));
                    if (t0.Count(t => t != null) == 0)
                    {
                        SplitJump(jumpBlock, new ValueOperand(rest), t1);
                    }
                    else if (t1.Count(t => t != null) == 0)
                    {
                        SplitJump(jumpBlock, new ValueOperand(rest), t0);
                    }
                    else
                    {
                        var b0 = method.GetBlock();
                        var b1 = method.GetBlock();

                        var sel = jumpBlock.Prepend(new Opcode(method.GetValue(), Op.Slice, value, bits - 1, bits - 1, 0, 0));

                        jumpBlock.Append(new Opcode(0, Op.BrCond, new ValueOperand(sel), new BlockOperand(b1), new BlockOperand(b0)));

                        SplitJump(b0, new ValueOperand(rest), t0);
                        SplitJump(b1, new ValueOperand(rest), t1);
                    }
                }
            }

            foreach (var blk in method.Blocks.Where(b => b.Instructions.Any(o => o.Op == Op.Switch)).ToList())
            {
                if (blk.Instructions.Count > 2)
                    blk.SplitBefore(blk.Instructions.First(i => i.Op == Op.Switch));
            }

            foreach (var blk in method.Blocks.Where(b => b.Instructions[0].Op == Op.Switch).ToList())
            {
                var sw = blk.Instructions[0];
                var br = blk.Instructions[1];

                var swB = method.GetBlock();

                var test = blk.Prepend(new Opcode(method.GetValue(), Op.Clt, sw.Operands.Count - 2, sw[0]));
                blk.InsertAfter(new Opcode(0, Op.BrCond, new ValueOperand(test), br[0], new BlockOperand(swB)), test);

                var targets = sw.Operands.Skip(1).ToArray();

                if (targets.Distinct().Count() < targets.Count())
                {
                    var largest = targets.GroupBy(x => x).OrderByDescending(x => x.Count()).First();
                    var lSet = largest.ToHashSet();

                    var indices = Enumerable.Range(0, targets.Length).Where(i => lSet.Contains(targets[i])).ToList();

                    var swSet = method.GetBlock();

                    var inSet = swB.Append(new Opcode(method.GetValue(), Op.InSet, new Operand[] { sw[0] }.Concat(indices.Select(i => new ConstOperand(i))).ToArray()));
                    swB.Append(new Opcode(0, Op.BrCond, new ValueOperand(inSet), largest.Key, new BlockOperand(swSet)));

                    foreach (var i in indices)
                        targets[i] = null;

                    swB = swSet;
                }

                SplitJump(swB, sw[0], targets);
            }
        }
    }

    class Program
    {
        static string CFG(Method m)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("digraph {");

            foreach (var blk in m.Blocks)
                foreach (var next in blk.NextBlocks())
                    sb.AppendLine($"BB{blk.Id} -> BB{next.Id};");

            sb.AppendLine("}");
            return sb.ToString();
        }

        static string DFG(Method m)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("digraph G {");

            foreach (var blk in m.AllInstructions())
            {
                foreach (var used in blk.Operands)
                {
                    void check(Operand u)
                    {
                        switch (u)
                        {
                            case ValueOperand vo:
                                sb.AppendLine($"n{vo.Value} -> n{blk.Result};");
                                break;
                            case PhiOperand vo:
                                check(vo.Value);
                                break;
                        }
                    }

                    check(used);
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        static void Main(string[] args)
        {
            var sw = Stopwatch.StartNew();

            // Resolve the type
            var asm = AssemblyDefinition.ReadAssembly(args[0]);
            var type = asm.FindType(args[1]);

            // Resolve the instance
            var asm2 = Assembly.LoadFrom(args[0]);
            var type2 = asm2.GetType(args[1]);

            var instance = Activator.CreateInstance(type2, null);

            // Build execute method
            var execute = type.Methods.Where(m => m.Name == "Execute").FirstOrDefault();
            var inp = new Interpreter(execute);

            CodePass.Process(inp.Method);

            CodePass.Process(inp.Method);
            if (inp.Method.FindConnectedComponents().Any(x => x.Count > 1)) throw new Exception("CDFG has loops. Not yet supported");

            CodePass.DoPass<Retype>(inp.Method); // Apply new type information

            CodePass.DoPass<FieldInlinePass>(inp.Method);

            CodePass.Process(inp.Method);

            CodePass.DoPass<HoistAll>(inp.Method);

            CodePass.DoPass<Retype>(inp.Method); // Apply new type information
            CodePass.Process(inp.Method);
            CodePass.DoPass<HoistAll>(inp.Method);
            CodePass.Process(inp.Method);

            /*CodePass.DoPass<InlineConditions>(inp.Method);
            CodePass.DoPass<CollapseControlFlow>(inp.Method);
            
            CodePass.Process(inp.Method);*/

            new Schedule { Settings = new ScheduleSettings { ArrayDelay = 1, RegDelay = 1, RequestDelay = 1 } }.Pass(inp.Method);
            //new VerilogPass { Settings = new VerilogSettings { Filename = @"output/out.v" } }.Pass(inp.Method);

            File.WriteAllText(@"output/cfg.txt", CFG(inp.Method).ToString());
            File.WriteAllText(@"output/dfg.txt", DFG(inp.Method).ToString());
            File.WriteAllText(@"output/asm.txt", (inp.Method).ToString());

            Process.Start(new ProcessStartInfo("/usr/bin/dot", "-Tpng -otest.png cfg.txt") { WorkingDirectory = @"output" });

            /*foreach (var scc in inp.Method.FindConnectedComponents().Where(x => x.Count > 1))
                Console.WriteLine(string.Join(", ", scc.Select(x => x.Id)) + ": " + scc.All(s => s.IsStateInvariant()));

            foreach (var s in CodePass.PassTime.OrderByDescending(o => o.Value))
                Console.WriteLine($"{s.Key}: {s.Value}");

            Console.WriteLine("Total time: {0}", sw.Elapsed.TotalSeconds);*/
        }
    }
}
