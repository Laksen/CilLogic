using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using CilLogic.Utilities;

namespace CilLogic.CodeModel
{
    public class Interpreter
    {
        public Method Method { get; }

        public void Dump()
        {
            Console.WriteLine(Method.ToString());
        }

        private class InstructionInfo
        {
            public Method Method { get; set; }

            public HashSet<Instruction> Priors = new HashSet<Instruction>();
            public HashSet<Instruction> Nexts = new HashSet<Instruction>();

            public Instruction NaturalNext = null;

            public int StackSize = 0, OutStackSize = 0;
            public bool Entered = false;

            public void ComeFrom(Instruction target)
            {
                Nexts.Add(target);
            }

            public void Enter(int stackSize, Instruction from)
            {
                if (from != null)
                    Priors.Add(from);

                if (Entered)
                {
                    if (StackSize != stackSize)
                        throw new Exception("Invalid stack size");
                }
                else
                {
                    Entered = true;
                    StackSize = stackSize;
                }
            }

            public void Leave(int stackSize)
            {
                OutStackSize = stackSize;
            }
        }

        private Dictionary<Instruction, T> Analyze<T>(List<Instruction> instructions, bool hasResult) where T : InstructionInfo, new()
        {
            var res = new Dictionary<Instruction, T>();
            foreach (var r in instructions) res[r] = new T() { Method = Method};

            var toVisit = new Queue<Tuple<int, Instruction>>();
            var visited = new HashSet<int>();

            toVisit.Enqueue(new Tuple<int, Instruction>(0, null));
            res[instructions[0]].Enter(0, null);

            while (toVisit.Count > 0)
            {
                var item = toVisit.Dequeue();
                var entry = item.Item1;

                if (visited.Contains(entry)) continue;
                int stackSize = res[instructions[entry]].StackSize;

                Instruction last = item.Item2;

                while ((entry < instructions.Count) && (entry >= 0))
                {
                    var ins = instructions[entry];
                    res[ins].Enter(stackSize, last);
                    last = ins;

                    visited.Add(entry);

                    entry++;

                    switch (ins.OpCode.StackBehaviourPop)
                    {
                        case StackBehaviour.Pop0: break;

                        case StackBehaviour.Popref:
                        case StackBehaviour.Popi:
                        case StackBehaviour.Pop1: stackSize--; break;

                        case StackBehaviour.Popref_pop1:
                        case StackBehaviour.Popref_popi:
                        case StackBehaviour.Popi_popi8:
                        case StackBehaviour.Pop1_pop1: stackSize -= 2; break;

                        case StackBehaviour.Popref_popi_popi:
                        case StackBehaviour.Popref_popi_popi8:
                        case StackBehaviour.Popref_popi_popref: stackSize -= 3; break;

                        case StackBehaviour.PopAll: stackSize = 0; break;

                        case StackBehaviour.Varpop:
                            {
                                if (ins.OpCode.Code == Code.Ret)
                                {
                                    stackSize -= hasResult ? 1 : 0;
                                }
                                else
                                {
                                    var m = (ins.Operand as MethodReference).Resolve();

                                    stackSize -= m.GetArgCount();
                                }
                                break;
                            }

                        default:
                            throw new NotSupportedException(ins.OpCode.StackBehaviourPop.ToString());
                    }

                    switch (ins.OpCode.StackBehaviourPush)
                    {
                        case StackBehaviour.Push0: break;

                        case StackBehaviour.Push1: stackSize += 1; break;

                        case StackBehaviour.Push1_push1: stackSize += 2; break;

                        case StackBehaviour.Pushi:
                        case StackBehaviour.Pushi8: stackSize += 1; break;

                        case StackBehaviour.Varpush:
                            {
                                var m = (ins.Operand as MethodReference).Resolve();
                                if (m.MethodReturnType.ReturnType != m.Module.TypeSystem.Void) stackSize++;
                                break;
                            }

                        default:
                            throw new NotSupportedException(ins.OpCode.StackBehaviourPush.ToString());
                    }

                    res[ins].Leave(stackSize);

                    res[ins].NaturalNext = ins.Next;

                    switch (ins.OpCode.FlowControl)
                    {
                        case FlowControl.Next:
                        case FlowControl.Call: break;
                        case FlowControl.Return:
                            {
                                entry = -1;
                                break;
                            }

                        case FlowControl.Branch:
                            {
                                var target = ins.Operand as Instruction;

                                toVisit.Enqueue(new Tuple<int, Instruction>(instructions.IndexOf(target), ins));
                                res[target].Enter(stackSize, ins);

                                entry = -1;

                                break;
                            }
                        case FlowControl.Cond_Branch:
                            {
                                if (ins.OpCode.Code == Code.Switch)
                                {
                                    foreach (var target in (ins.Operand as Instruction[]))
                                    {
                                        toVisit.Enqueue(new Tuple<int, Instruction>(instructions.IndexOf(target), ins));
                                        res[target].Enter(stackSize, ins);
                                    }
                                }
                                else
                                {
                                    var target = ins.Operand as Instruction;

                                    toVisit.Enqueue(new Tuple<int, Instruction>(instructions.IndexOf(target), ins));
                                    res[target].Enter(stackSize, ins);
                                }

                                break;
                            }
                        default:
                            throw new NotSupportedException(ins.OpCode.FlowControl.ToString());
                    }
                }
            }

            return res;
        }

        private class InstrInfo : InstructionInfo
        {
            public BasicBlock Block;
            public Dictionary<Instruction, Operand[]> PreStacks = new Dictionary<Instruction, Operand[]>();
            public int[] PreCollectStack;

            public int[] OutStack;

            public void AllocInfo(Method method)
            {
                Block = method.GetBlock();
                Block.Append(new Opcode(0, Op.Nop));
                PreStacks = new Dictionary<Instruction, Operand[]>();
                PreCollectStack = Enumerable.Range(0, StackSize).Select(i => method.GetValue()).ToArray();
                OutStack = Enumerable.Range(0, OutStackSize).Select(i => method.GetValue()).ToArray();
            }

            public void InsertPhis(Dictionary<Instruction, BasicBlock> blockSelector)
            {
                if (PreStacks.Count == 0)
                    foreach (int i in Enumerable.Range(0, StackSize))
                        Block.Prepend(new Opcode(PreCollectStack[i], Op.Mov, 0));
                else if (PreStacks.Count == 1)
                    foreach (int i in Enumerable.Range(0, StackSize))
                        Block.Prepend(new Opcode(PreCollectStack[i], Op.Mov, PreStacks.Values.Single()[i]));
                else
                    foreach (int i in Enumerable.Range(0, StackSize))
                        Block.Prepend(new Opcode(PreCollectStack[i], Op.Phi, PreStacks.Select(x => new PhiOperand(blockSelector[x.Key], x.Value[i])).ToArray()));
            }

            private Stack<Operand> stack;

            private Operand Pop()
            {
                return stack.Pop();
            }

            private void Push(Opcode i)
            {
                stack.Push(new ValueOperand(i.Result, i.GetResultType(Method)));
            }

            private Opcode Observe(Opcode o)
            {
                Block.Append(o);

                return o;
            }

            private void Convert(Method Method, int sign, int width)
            {
                Push(Observe(new Opcode(Method.GetValue(), Op.Conv, Pop(), sign, width * 8)));
            }

            public void Execute(Instruction ins, bool hasResult, Dictionary<Instruction, Opcode> jumpPoints, Method method, List<Operand> arguments, IList<VariableDefinition> variables)
            {
                stack = new Stack<Operand>(PreCollectStack.Select((x, i) => new ValueOperand(x, TypeDef.Unknown)));

                switch (ins.OpCode.Code)
                {
                    case Code.Nop: Observe(Opcode.Nop); break;
                    case Code.Ret:
                        {
                            if (hasResult)
                                Observe(new Opcode(0, Op.Return, Pop()));
                            else
                                Observe(new Opcode(0, Op.Return));
                            break;
                        }

                    case Code.Switch: Observe(new Opcode(0, Op.Switch, new Operand[] { Pop() }.Concat(((Instruction[])ins.Operand).Select<Instruction, Operand>(x => jumpPoints[x])).ToArray())); break;

                    case Code.Br: case Code.Br_S: Observe(new Opcode(0, Op.Br, jumpPoints[ins.Operand as Instruction])); break;

                    case Code.Brfalse: case Code.Brfalse_S: Observe(new Opcode(0, Op.BrFalse, Pop(), jumpPoints[ins.Operand as Instruction])); break;
                    case Code.Brtrue: case Code.Brtrue_S: Observe(new Opcode(0, Op.BrTrue, Pop(), jumpPoints[ins.Operand as Instruction])); break;

                    case Code.Dup: stack.Push(stack.Peek()); break;
                    case Code.Pop: Pop(); break;

                    case Code.Beq_S: case Code.Beq: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Ceq, v1, v2))), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Bne_Un_S: case Code.Bne_Un: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Ceq, v1, v2))), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Bge: case Code.Bge_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Clt, v2, v1))), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Bge_Un: case Code.Bge_Un_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Cltu, v2, v1))), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Ble: case Code.Ble_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Clt, v2, v1))), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Ble_Un: case Code.Ble_Un_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Cltu, v2, v1))), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Bgt: case Code.Bgt_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Clt, v2, v1))), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Bgt_Un: case Code.Bgt_Un_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Cltu, v2, v1))), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Blt: case Code.Blt_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Clt, v1, v2))), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Blt_Un: case Code.Blt_Un_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, new ValueOperand(Observe(new Opcode(method.GetValue(), Op.Cltu, v1, v2))), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Ceq: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Ceq, a, b))); break; }
                    case Code.Cgt: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Clt, b, a))); break; }
                    case Code.Cgt_Un: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Cltu, b, a))); break; }
                    case Code.Clt: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Clt, b, a))); break; }
                    case Code.Clt_Un: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Cltu, b, a))); break; }

                    case Code.Add: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Add, a, b))); break; }
                    case Code.Sub: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Sub, a, b))); break; }
                    case Code.And: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.And, a, b))); break; }
                    case Code.Or: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Or, a, b))); break; }
                    case Code.Xor: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Xor, a, b))); break; }
                    case Code.Shl: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Lsl, a, b))); break; }
                    case Code.Shr: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Asr, a, b))); break; }
                    case Code.Shr_Un: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(method.GetValue(), Op.Lsr, a, b))); break; }

                    case Code.Ldarg_0: stack.Push(arguments[0]); break;
                    case Code.Ldarg_1: stack.Push(arguments[1]); break;
                    case Code.Ldarg_2: stack.Push(arguments[2]); break;
                    case Code.Ldarg_3: stack.Push(arguments[3]); break;
                    case Code.Ldarg_S:
                    case Code.Ldarg: stack.Push(arguments[(ins.Operand as ParameterDefinition).Index]); break;

                    case Code.Ldc_I4_S: stack.Push((int)(sbyte)ins.Operand); break;
                    case Code.Ldc_I4: stack.Push((int)ins.Operand); break;
                    case Code.Ldc_I4_0: stack.Push(0); break;
                    case Code.Ldc_I4_1: stack.Push(1); break;
                    case Code.Ldc_I4_2: stack.Push(2); break;
                    case Code.Ldc_I4_3: stack.Push(3); break;
                    case Code.Ldc_I4_4: stack.Push(4); break;
                    case Code.Ldc_I4_5: stack.Push(5); break;
                    case Code.Ldc_I4_6: stack.Push(6); break;
                    case Code.Ldc_I4_7: stack.Push(7); break;
                    case Code.Ldc_I4_8: stack.Push(8); break;

                    case Code.Ldelem_Any:
                    case Code.Ldelem_I:
                    case Code.Ldelem_I1:
                    case Code.Ldelem_I2:
                    case Code.Ldelem_I4:
                    case Code.Ldelem_I8:
                    case Code.Ldelem_U1:
                    case Code.Ldelem_U2:
                    case Code.Ldelem_U4:
                        {
                            var index = Pop();
                            var arrRef = Pop();

                            Push(Observe(new Opcode(method.GetValue(), Op.LdArray, arrRef, index)));
                            break;
                        }

                    case Code.Stelem_Any:
                    case Code.Stelem_I:
                    case Code.Stelem_I1:
                    case Code.Stelem_I2:
                    case Code.Stelem_I4:
                    case Code.Stelem_I8:
                        {
                            var value = Pop();
                            var index = Pop();
                            var arrRef = Pop();

                            Push(Observe(new Opcode(0, Op.StArray, arrRef, index, value)));
                            break;
                        }

                    case Code.Conv_I: Convert(method, 1, 8); break;
                    case Code.Conv_I1: Convert(method, 1, 1); break;
                    case Code.Conv_I2: Convert(method, 1, 2); break;
                    case Code.Conv_I4: Convert(method, 1, 4); break;
                    case Code.Conv_I8: Convert(method, 1, 8); break;

                    case Code.Conv_Ovf_I: Convert(method, 1, 8); break;
                    case Code.Conv_Ovf_I1: Convert(method, 1, 1); break;
                    case Code.Conv_Ovf_I2: Convert(method, 1, 2); break;
                    case Code.Conv_Ovf_I4: Convert(method, 1, 4); break;
                    case Code.Conv_Ovf_I8: Convert(method, 1, 8); break;

                    case Code.Conv_Ovf_I_Un: Convert(method, 1, 8); break;
                    case Code.Conv_Ovf_I1_Un: Convert(method, 1, 1); break;
                    case Code.Conv_Ovf_I2_Un: Convert(method, 1, 2); break;
                    case Code.Conv_Ovf_I4_Un: Convert(method, 1, 4); break;
                    case Code.Conv_Ovf_I8_Un: Convert(method, 1, 8); break;

                    case Code.Conv_U: Convert(method, 0, 8); break;
                    case Code.Conv_U1: Convert(method, 0, 1); break;
                    case Code.Conv_U2: Convert(method, 0, 2); break;
                    case Code.Conv_U4: Convert(method, 0, 4); break;
                    case Code.Conv_U8: Convert(method, 0, 8); break;

                    case Code.Conv_Ovf_U: Convert(method, 0, 8); break;
                    case Code.Conv_Ovf_U1: Convert(method, 0, 1); break;
                    case Code.Conv_Ovf_U2: Convert(method, 0, 2); break;
                    case Code.Conv_Ovf_U4: Convert(method, 0, 4); break;
                    case Code.Conv_Ovf_U8: Convert(method, 0, 8); break;

                    case Code.Conv_Ovf_U_Un: Convert(method, 0, 8); break;
                    case Code.Conv_Ovf_U1_Un: Convert(method, 0, 1); break;
                    case Code.Conv_Ovf_U2_Un: Convert(method, 0, 2); break;
                    case Code.Conv_Ovf_U4_Un: Convert(method, 0, 4); break;
                    case Code.Conv_Ovf_U8_Un: Convert(method, 0, 8); break;

                    case Code.Call:
                    case Code.Callvirt:
                        {
                            var m = (ins.Operand as MethodReference);
                            var md = m.Resolve();

                            var v = 0;
                            if (md.MethodReturnType.ReturnType != m.Module.TypeSystem.Void) v = method.GetValue();

                            var op = Observe(new Opcode(v, Op.Call, new Operand[] { new MethodOperand(m, method) }.Concat(Enumerable.Range(0, md.GetArgCount()).Select(i => Pop()).ToArray().Reverse()).ToArray()));

                            if (md.MethodReturnType.ReturnType != m.Module.TypeSystem.Void) Push(op);

                            break;
                        }

                    case Code.Ldloca_S:
                    case Code.Ldloca:
                        {
                            var v = (ins.Operand as VariableReference);
                            stack.Push(new LocOperand(v.Index, v.VariableType, method)); break;
                        }

                    case Code.Ldloc_0: Push(Observe(new Opcode(method.GetValue(), Op.LdLoc, 0, new TypeOperand(variables[0].VariableType, method)))); break;
                    case Code.Ldloc_1: Push(Observe(new Opcode(method.GetValue(), Op.LdLoc, 1, new TypeOperand(variables[1].VariableType, method)))); break;
                    case Code.Ldloc_2: Push(Observe(new Opcode(method.GetValue(), Op.LdLoc, 2, new TypeOperand(variables[2].VariableType, method)))); break;
                    case Code.Ldloc_3: Push(Observe(new Opcode(method.GetValue(), Op.LdLoc, 3, new TypeOperand(variables[3].VariableType, method)))); break;
                    case Code.Ldloc_S:
                    case Code.Ldloc:
                        {
                            var v = ins.Operand as VariableReference;
                            Push(Observe(new Opcode(method.GetValue(), Op.LdLoc, v.Index, new TypeOperand(v.VariableType, method))));
                            break;
                        }

                    case Code.Stloc_0: Observe(new Opcode(0, Op.StLoc, 0, Pop())); break;
                    case Code.Stloc_1: Observe(new Opcode(0, Op.StLoc, 1, Pop())); break;
                    case Code.Stloc_2: Observe(new Opcode(0, Op.StLoc, 2, Pop())); break;
                    case Code.Stloc_3: Observe(new Opcode(0, Op.StLoc, 3, Pop())); break;
                    case Code.Stloc_S:
                    case Code.Stloc: Observe(new Opcode(0, Op.StLoc, (ins.Operand as VariableReference).Index, Pop())); break;

                    case Code.Ldfld:
                        {
                            Push(Observe(new Opcode(method.GetValue(), Op.LdFld, Pop(), new FieldOperand((FieldReference)ins.Operand, method))));
                            break;
                        }
                    case Code.Stfld:
                        {
                            var value = Pop();
                            var obj = Pop();
                            Observe(new Opcode(method.GetValue(), Op.StFld, obj, new FieldOperand((FieldReference)ins.Operand, method), value));
                            break;
                        }

                    case Code.Initobj:
                        {
                            var typ = (ins.Operand as TypeReference);

                            var addr = Pop();
                            Observe(new Opcode(0, Op.InitObj, addr, new TypeOperand(typ, method)));
                            break;
                        }

                    default:
                        throw new Exception("Unhandled OP: " + ins);
                }

                foreach (var i in Enumerable.Range(0, OutStackSize))
                    Block.Prepend(new Opcode(OutStack[OutStackSize - i - 1], Op.Mov, stack.Pop()));
            }
        }

        private void Execute(bool hasThis, bool hasResult, List<Instruction> instructions, List<Operand> arguments, IList<VariableDefinition> variables)
        {
            // First insert jump targets
            var block = Method.Entry;

            // Analyze priors and stack sizes
            var info = Analyze<InstrInfo>(instructions, hasResult);
            foreach (var i in info) i.Value.AllocInfo(Method);

            // Analyze nexts
            foreach (var i in info) i.Value.Priors.ToList().ForEach(v => info[v].ComeFrom(i.Key));

            // Execute each instruction
            var jumpPoints = info.ToDictionary(x => x.Key, x => x.Value.Block.Instructions.First());
            foreach (var i in info)
                i.Value.Execute(i.Key, hasResult, jumpPoints, Method, arguments, variables);

            // Add optional branches
            foreach (var i in info)
                if (i.Value.NaturalNext != null)
                    i.Value.Block.Append(new Opcode(0, Op.Br, jumpPoints[i.Value.NaturalNext]));

            // Update pre stacks
            foreach (var i in info)
                foreach (var next in i.Value.Nexts)
                    info[next].PreStacks[i.Key] = i.Value.OutStack.Select((x, i2) => new ValueOperand(x, TypeDef.Unknown)).ToArray();

            // Insert phi nodes
            var blockSelector = info.ToDictionary(x => x.Key, x => x.Value.Block);
            foreach (var i in info)
                i.Value.InsertPhis(blockSelector);

            Method.Entry.Append(new Opcode(0, Op.Br, new BlockOperand(info.Values.First().Block)));

            Method.Fragment();
        }

        public Interpreter(MethodReference method, List<Operand> arguments = default(List<Operand>), Dictionary<GenericParameter, TypeDefinition> genArgs = default(Dictionary<GenericParameter, TypeDefinition>))
        {
            var md = method.Resolve();

            Method = new Method(md, method, genArgs);

            if (arguments == null)
            {
                arguments = new List<Operand>();
                if (method.HasThis)
                    arguments.Add(new SelfOperand(new CecilType<TypeDefinition>(md.DeclaringType, Method)));
            }

            Execute(method.HasThis, method.MethodReturnType.ReturnType != method.Module.TypeSystem.Void, md.Body.Instructions.ToList(), arguments, md.Body.Variables);
        }
    }
}