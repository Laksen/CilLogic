using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace CilLogic.CodeModel
{
    public class Interpreter
    {
        public Method Method { get; }

        public void Dump()
        {
            Console.WriteLine(Method.ToString());
        }

        private void Execute(bool hasThis, IEnumerable<Instruction> instructions)
        {
            // First insert jump points
            var block = Method.Entry;
            var jumpPoints = instructions.ToDictionary(i => i, i => (Opcode)null);

            foreach (var ins in instructions)
                jumpPoints[ins] = block.Append(Opcode.Nop);

            var stack = new Stack<Operand>();

            Func<Operand> Pop = () => stack.Pop();
            Action<Opcode> Push = i => stack.Push(new ValueOperand(i.Result));

            // Execute
            foreach (var ins in instructions)
            {
                var pt = jumpPoints[ins];
                Func<Opcode, Opcode> Observe = o => 
                {
                    block.InsertAfter(o, pt);
                    pt = o;
                    return o;
                };

                Action<int, int> Convert = (s,w) => Push(Observe(new Opcode(Method.GetValue(), Op.Conv, Pop(), s, w)));

                Console.WriteLine(ins);

                switch (ins.OpCode.Code)
                {
                    case Code.Nop: Observe(Opcode.Nop); break;
                    case Code.Ret: Observe(new Opcode(0, Op.Return)); break;

                    case Code.Switch: Observe(new Opcode(0, Op.Switch, new Operand[]{ Pop()}.Concat(((Instruction[])ins.Operand).Select<Instruction, Operand>(x => jumpPoints[x])).ToArray())); break;

                    case Code.Br: case Code.Br_S: Observe(new Opcode(0, Op.Br, jumpPoints[ins.Operand as Instruction])); break;

                    case Code.Brfalse: case Code.Brfalse_S: Observe(new Opcode(0, Op.BrFalse, Pop(), jumpPoints[ins.Operand as Instruction])); break;
                    case Code.Brtrue: case Code.Brtrue_S: Observe(new Opcode(0, Op.BrTrue, Pop(), jumpPoints[ins.Operand as Instruction])); break;

                    case Code.Dup: stack.Push(stack.Peek()); break;
                    case Code.Pop: Pop(); break;

                    case Code.Beq_S: case Code.Beq:       { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, Observe(new Opcode(Method.GetValue(), Op.Ceq, v1,v2)), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Bne_Un_S: case Code.Bne_Un: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, Observe(new Opcode(Method.GetValue(), Op.Ceq, v1,v2)), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Bge: case Code.Bge_S:       { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, Observe(new Opcode(Method.GetValue(), Op.Clt, v2, v1)), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Bge_Un: case Code.Bge_Un_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, Observe(new Opcode(Method.GetValue(), Op.Cltu, v2,v1)), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Ble: case Code.Ble_S:       { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, Observe(new Opcode(Method.GetValue(), Op.Clt, v2, v1)), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Ble_Un: case Code.Ble_Un_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrFalse, Observe(new Opcode(Method.GetValue(), Op.Cltu, v2, v1)), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Bgt: case Code.Bgt_S:       { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, Observe(new Opcode(Method.GetValue(), Op.Clt, v2, v1)), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Bgt_Un: case Code.Bgt_Un_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, Observe(new Opcode(Method.GetValue(), Op.Cltu, v2, v1)), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Blt: case Code.Blt_S:       { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, Observe(new Opcode(Method.GetValue(), Op.Clt, v1, v2)), jumpPoints[ins.Operand as Instruction])); break; }
                    case Code.Blt_Un: case Code.Blt_Un_S: { var v2 = Pop(); var v1 = Pop(); Observe(new Opcode(0, Op.BrTrue, Observe(new Opcode(Method.GetValue(), Op.Cltu, v1, v2)), jumpPoints[ins.Operand as Instruction])); break; }

                    case Code.Ceq:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Ceq, a, b))); break; }
                    case Code.Cgt:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Clt, b, a))); break; }
                    case Code.Cgt_Un: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Cltu, b, a))); break; }
                    case Code.Clt:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Clt, b, a))); break; }
                    case Code.Clt_Un: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Cltu, b, a))); break; }

                    case Code.Add:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Add, a, b))); break; }
                    case Code.Sub:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Sub, a, b))); break; }
                    case Code.And:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.And, a, b))); break; }
                    case Code.Or:     { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Or, a, b))); break; }
                    case Code.Xor:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Xor, a, b))); break; }
                    case Code.Shl:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Lsl, a, b))); break; }
                    case Code.Shr:    { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Asr, a, b))); break; }
                    case Code.Shr_Un: { var b = Pop(); var a = Pop(); Push(Observe(new Opcode(Method.GetValue(), Op.Lsr, a, b))); break; }

                    case Code.Ldarg_0: if (hasThis) stack.Push(new SelfOperand()); else Push(Observe(new Opcode(Method.GetValue(), Op.LdArg, 0))); break;
                    case Code.Ldarg_1: Push(Observe(new Opcode(Method.GetValue(), Op.LdArg, 1))); break;
                    case Code.Ldarg_2: Push(Observe(new Opcode(Method.GetValue(), Op.LdArg, 2))); break;
                    case Code.Ldarg_3: Push(Observe(new Opcode(Method.GetValue(), Op.LdArg, 3))); break;
                    case Code.Ldarg_S:
                    case Code.Ldarg: Push(Observe(new Opcode(Method.GetValue(), Op.LdArg, (int)ins.Operand))); break;

                    case Code.Ldc_I4_S: stack.Push((int)(sbyte)ins.Operand); break;
                    case Code.Ldc_I4:   stack.Push((int)ins.Operand); break;
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

                            Push(Observe(new Opcode(Method.GetValue(), Op.LdArray, arrRef, index)));
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

                    case Code.Conv_I:  Convert(1,8); break;
                    case Code.Conv_I1: Convert(1,1); break;
                    case Code.Conv_I2: Convert(1,2); break;
                    case Code.Conv_I4: Convert(1,4); break;
                    case Code.Conv_I8: Convert(1,8); break;

                    case Code.Conv_Ovf_I:  Convert(1,8); break;
                    case Code.Conv_Ovf_I1: Convert(1,1); break;
                    case Code.Conv_Ovf_I2: Convert(1,2); break;
                    case Code.Conv_Ovf_I4: Convert(1,4); break;
                    case Code.Conv_Ovf_I8: Convert(1,8); break;

                    case Code.Conv_Ovf_I_Un:  Convert(1,8); break;
                    case Code.Conv_Ovf_I1_Un: Convert(1,1); break;
                    case Code.Conv_Ovf_I2_Un: Convert(1,2); break;
                    case Code.Conv_Ovf_I4_Un: Convert(1,4); break;
                    case Code.Conv_Ovf_I8_Un: Convert(1,8); break;

                    case Code.Conv_U:  Convert(0,8); break;
                    case Code.Conv_U1: Convert(0,1); break;
                    case Code.Conv_U2: Convert(0,2); break;
                    case Code.Conv_U4: Convert(0,4); break;
                    case Code.Conv_U8: Convert(0,8); break;

                    case Code.Conv_Ovf_U:  Convert(0,8); break;
                    case Code.Conv_Ovf_U1: Convert(0,1); break;
                    case Code.Conv_Ovf_U2: Convert(0,2); break;
                    case Code.Conv_Ovf_U4: Convert(0,4); break;
                    case Code.Conv_Ovf_U8: Convert(0,8); break;

                    case Code.Conv_Ovf_U_Un:  Convert(0,8); break;
                    case Code.Conv_Ovf_U1_Un: Convert(0,1); break;
                    case Code.Conv_Ovf_U2_Un: Convert(0,2); break;
                    case Code.Conv_Ovf_U4_Un: Convert(0,4); break;
                    case Code.Conv_Ovf_U8_Un: Convert(0,8); break;

                    case Code.Call:
                        {
                            var m = (ins.Operand as MethodReference).Resolve();

                            var v = 0;
                            if (m.MethodReturnType.ReturnType != m.Module.TypeSystem.Void) v = Method.GetValue();
                            
                            var op = Observe(new Opcode(v, Op.Call, new Operand[]{ new MethodOperand(m) }.Concat(Enumerable.Range(0, GetArgCount(m)).Select(i => Pop())).ToArray()));

                            if (m.MethodReturnType.ReturnType != m.Module.TypeSystem.Void) Push(op);

                            break;
                        }

                    case Code.Ldloc_0: Push(Observe(new Opcode(Method.GetValue(), Op.LdLoc, 0))); break;
                    case Code.Ldloc_1: Push(Observe(new Opcode(Method.GetValue(), Op.LdLoc, 1))); break;
                    case Code.Ldloc_2: Push(Observe(new Opcode(Method.GetValue(), Op.LdLoc, 2))); break;
                    case Code.Ldloc_3: Push(Observe(new Opcode(Method.GetValue(), Op.LdLoc, 3))); break;
                    case Code.Ldloc_S:
                    case Code.Ldloc: Push(Observe(new Opcode(Method.GetValue(), Op.LdLoc,(ins.Operand as VariableReference).Index))); break;

                    case Code.Stloc_0: Observe(new Opcode(0, Op.StLoc, 0, Pop())); break;
                    case Code.Stloc_1: Observe(new Opcode(0, Op.StLoc, 1, Pop())); break;
                    case Code.Stloc_2: Observe(new Opcode(0, Op.StLoc, 2, Pop())); break;
                    case Code.Stloc_3: Observe(new Opcode(0, Op.StLoc, 3, Pop())); break;
                    case Code.Stloc_S:
                    case Code.Stloc: Observe(new Opcode(0, Op.StLoc, (ins.Operand as VariableReference).Index, Pop())); break;

                    case Code.Ldfld: Push(Observe(new Opcode(Method.GetValue(), Op.LdFld, Pop(), new FieldOperand((FieldReference)ins.Operand)))); break;
                    case Code.Stfld:
                        {
                            var value = Pop();
                            var obj = Pop();
                            Observe(new Opcode(Method.GetValue(), Op.StFld, obj, new FieldOperand((FieldReference)ins.Operand), value));
                            break;
                        }

                    default:
                        throw new Exception("Unhandled OP: " + ins);
                }
            }
        }

        private int GetArgCount(MethodDefinition m)
        {
            return (m.IsStatic ? 0 : 1) + m.Parameters.Count;
        }

        public Interpreter(MethodDefinition method)
        {
            Method = new Method();

            Execute(method.HasThis, method.Body.Instructions);
        }
    }
}