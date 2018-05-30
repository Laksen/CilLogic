using System;
using System.Collections.Generic;
using System.Linq;
using CilLogic.Utilities;
using Mono.Cecil;

namespace CilLogic.CodeModel.Passes
{
    public class InlinePass : CodePass
    {
        public override void Pass(Method method)
        {
            var calls = method.AllInstructions().Where(i => i.Op == Op.Call).ToList();

            foreach (var call in calls)
            {
                var nextBlock = call.Block.SplitBefore(call.Next);
                var result = call.Result;

                var obj = call[1] as SelfOperand;
                var func = call[0] as MethodOperand;

                var md = func.Method.Resolve();

                if (md.CustomAttributes.Any(o => o.AttributeType.Name == "OperationAttribute"))
                {
                    var val = (Op)md.CustomAttributes[0].ConstructorArguments[0].Value;

                    call.Block.Replace(call, new Opcode(call.Result, val, call.Operands.Skip(1).ToArray()));
                    
                    continue;
                }

                //if (obj == null) throw new NotSupportedException("Non-self operands are not supported");

                Dictionary<GenericParameter, TypeDefinition> genPara = new Dictionary<GenericParameter, TypeDefinition>();
                if (md.HasGenericParameters && func.Method.IsGenericInstance)
                {
                    for(int i=0; i<md.GenericParameters.Count; i++)
                    genPara[md.GenericParameters[i]] = (func.Method as IGenericInstance).GenericArguments[i].Resolve(method.GenericParams);
                    //var args = (func.Method as func.
                }

                var m = new Interpreter(func.Method, call.Operands.Skip(1).ToList(), genPara).Method;

                CodePass.Process(m);

                call.Block.Replace(call, new Opcode(0, Op.Br, new BlockOperand(m.Entry)));

                var returns = m.AllInstructions().Where(x => x.Op == Op.Return).ToList();

                if (result != 0)
                    nextBlock.Prepend(new Opcode(result, Op.Phi, returns.Select(r => new PhiOperand(r.Block, r[0])).ToArray()));
                    
                returns.ForEach(r => r.Block.Replace(r, new Opcode(0, Op.Br, new BlockOperand(nextBlock))));

                m.Blocks.ForEach(b => b.Method = method);
                method.Blocks.AddRange(m.Blocks);
            }
        }
    }
}