using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CilLogic.CodeModel;
using CilLogic.CodeModel.Passes;
using CilLogic.Utilities;

using Mono.Cecil;

namespace CilLogic
{
    class Program
    {
        static string FlowGraph(Method m)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("digraph {");

            foreach (var blk in m.Blocks)
                foreach (var next in blk.NextBlocks())
                    sb.AppendLine($"BB{blk.Id} -> BB{next.Id};");

            sb.AppendLine("}");
            return sb.ToString();
        }

        static void Main(string[] args)
        {
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

            File.WriteAllText(@"C:\Users\jepjoh2\Desktop\New Text Document.txt", (inp.Method).ToString());

            foreach (var scc in inp.Method.FindConnectedComponents().Where(x => x.Count > 1))
                Console.WriteLine(string.Join(", ", scc.Select(x => x.Id)) + ": " + scc.All(s => s.IsStateInvariant()));

            foreach (var s in CodePass.PassTime.OrderByDescending(o => o.Value))
                Console.WriteLine($"{s.Key}: {s.Value}");
        }
    }
}
