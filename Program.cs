using System;
using System.Linq;
using System.Reflection;
using CilLogic.CodeModel;
using CilLogic.CodeModel.Passes;
using CilLogic.Utilities;

using Mono.Cecil;

namespace CilLogic
{
    class Program
    {
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

            new PassDeadCode().Pass(inp.Method);
            new PassPeephole().Pass(inp.Method);
            new PassDeadCode().Pass(inp.Method);

            Console.WriteLine(inp.Method);
        }
    }
}
