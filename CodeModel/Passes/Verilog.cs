using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CilLogic.Types;
using CilLogic.Utilities;
using Mono.Cecil;

namespace CilLogic.CodeModel.Passes
{
    public class VerilogSettings
    {
        public string Filename = "out.v";
    }

    public class WriteConsolidationPass : CodePass
    {
        public override void Pass(Method method)
        {
            var addedSelect = false;

            var fieldStores = method.AllInstructions().Where(x => x.Op == Op.StFld).ToList();
            foreach (var multiWriters in fieldStores.GroupBy(x => x[1]).Where(x => x.Count() > 1))
            {
                var old = multiWriters.ToList();

                var condition = method.Entry.Prepend(new Opcode(method.GetValue(), Op.Or, old.Select(x => x[3]).ToArray()));
                var value = method.Entry.Prepend(new Opcode(method.GetValue(), Op.Select, old.Select(x => new CondValue(x[3], x[2], x[2].OperandType)).ToArray()));

                method.Entry.Prepend(new Opcode(0, Op.StFld, old.First()[0], old.First()[1], new ValueOperand(value), new ValueOperand(condition)));
                old.ForEach(x => x.Block.Replace(x, new Opcode(0, Op.Nop)));

                addedSelect = true;
            }

            var allRequests = method.AllInstructions().Where(x => x.Op == Op.Request).ToList();
            foreach (var requests in allRequests.GroupBy(x => x[0]).Where(x => x.Count() > 1))
            {
                var old = requests.ToList();

                var condition = method.Entry.Prepend(new Opcode(method.GetValue(), Op.Or, old.Select(x => x[2]).ToArray()));
                var value = method.Entry.Prepend(new Opcode(method.GetValue(), Op.Select, old.Select(x => new CondValue(x[2], x[1], x[1].OperandType)).ToArray()));

                var req = new Opcode(method.GetValue(), Op.Request, old.First()[0], new ValueOperand(value), new ValueOperand(condition));
                method.Entry.Prepend(req);
                old.ForEach(x => x.Block.Replace(x, new Opcode(x.Result, Op.Mov, new ValueOperand(req))));

                addedSelect = true;
            }

            var allStArrays = method.AllInstructions().Where(x => x.Op == Op.StArray).ToList();
            foreach (var starray in allStArrays.GroupBy(x => x[0]).Where(x => x.Count() > 1))
            {
                var old = starray.ToList();

                var condition = method.Entry.Prepend(new Opcode(method.GetValue(), Op.Or, old.Select(x => x[3]).ToArray()));
                var value = method.Entry.Prepend(new Opcode(method.GetValue(), Op.Select, old.Select(x => new CondValue(x[3], x[2], x[2].OperandType)).ToArray()));
                var index = method.Entry.Prepend(new Opcode(method.GetValue(), Op.Select, old.Select(x => new CondValue(x[3], x[1], x[1].OperandType)).ToArray()));

                var req = new Opcode(0, Op.StArray, starray.Key, new ValueOperand(index), new ValueOperand(value), new ValueOperand(condition));
                method.Entry.Prepend(req);
                old.ForEach(x => x.Block.Replace(x, new Opcode(x.Result, Op.Mov, new ValueOperand(req))));

                addedSelect = true;
            }

            if (addedSelect)
            {
                CodePass.DoPass<PassDeadCode>(method);
                CodePass.DoPass<PassPeephole>(method);
                CodePass.DoPass<PassDeadCode>(method);
            }
        }
    }

    public class VerilogPass : CodePass
    {
        private class PortDefinition
        {
            public string name;
            public int width;
            public bool signed, input, registered;

            public bool is_valid, is_ready;
        }

        public VerilogSettings Settings = new VerilogSettings();

        private IEnumerable<PortDefinition> GetPorts(Method method, TypeDefinition arg, bool inDir, string prefix)
        {
            if (arg.IsSimple())
                yield return new PortDefinition { name = prefix + "data", input = inDir, registered = false, signed = arg.GetSign(method), width = arg.GetWidth(method) };
            else
            {
                foreach (var portField in arg.Fields)
                    yield return new PortDefinition { name = prefix + portField.Name, input = inDir, registered = false, signed = portField.FieldType.GetSign(method, arg), width = portField.GetWidth(method) };
            }

            //yield return new PortDefinition { name = prefix + "valid", input = inDir, registered = false, signed = false, width = 1, is_valid = true };
            //yield return new PortDefinition { name = prefix + "ready", input = !inDir, registered = false, signed = false, width = 1, is_ready = true };
        }

        private IEnumerable<PortDefinition> GetPorts(Method method, FieldDefinition field)
        {
            var prefix = field.Name + "_";
            var iprefix = "";
            var oprefix = "";

            if (field.CustomAttributes.Any(o => o.AttributeType.Name == "PortPrefixAttribute"))
            {
                var pref = field.CustomAttributes.First(o => o.AttributeType.Name == "PortPrefixAttribute");
                prefix = (string)pref.ConstructorArguments[0].Value + "_";
                oprefix = (string)pref.ConstructorArguments[1].Value;
                iprefix = (string)pref.ConstructorArguments[2].Value;
            }

            var ft = field.FieldType.Resolve(field.FieldType, method.GenericParams);

            if (ft.IsRequestPort())
            {
                var key = ft.GenericParameters.First().Resolve(field.FieldType, method.GenericParams);
                var value = ft.GenericParameters.Last().Resolve(field.FieldType, method.GenericParams);

                foreach (var o in GetPorts(method, key, false, prefix + oprefix)) yield return o;
                foreach (var o in GetPorts(method, value, true, prefix + iprefix)) yield return o;
            }
            else
            {
                var key = ft.GenericParameters.First().Resolve(field.FieldType, method.GenericParams);

                foreach (var o in GetPorts(method, key, ft.IsInPort(), prefix)) yield return o;
            }
        }

        private IEnumerable<IEnumerable<string>> GetPorts(Method method)
        {
            string PortToString(PortDefinition r)
            {
                var dir = r.input ? "input" : "output";
                var width = r.width > 1 ? $"[{r.width - 1}:0] " : "";
                var reg = !r.input && r.registered ? "reg " : "";

                return $"{dir} {reg}{width}{r.name}";
            }

            yield return new[] { PortToString(new PortDefinition { name = "Clock", input = true, signed = false, width = 1 }) };

            var type = method.MethodRef.DeclaringType.Resolve();

            foreach (var x in type.Fields.Where(x => x.FieldType.Resolve().IsPort()).Select(p => GetPorts(method, p).Select(PortToString)))
                yield return x;
        }

        public override void Pass(Method method)
        {
            CodePass.DoPass<WriteConsolidationPass>(method, ">");

            string getWidth(int width)
            {
                if (width <= 1)
                    return "";
                else
                    return $"[{width - 1}:0] ";
            }

            string Get(Operand oper)
            {
                if (oper is ValueOperand vo)
                    return "res" + vo.Value;
                else if (oper is CondValue cv)
                    return Get(cv.Condition) + " ? " + Get(cv.Value);
                else if (oper is FieldOperand fo)
                    return fo.Field.Name;
                else
                    return oper.ToString();
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine(string.Format("module {0}(", method.MethodRef.DeclaringType.Name));
            sb.AppendLine(string.Join("," + Environment.NewLine + Environment.NewLine, GetPorts(method).Select(x => string.Join("," + Environment.NewLine, x.Select(u => "    " + u)))));
            sb.AppendLine(");");

            // Define state
            foreach (var fld in method.AllInstructions().SelectMany(x => x.Operands).OfType<FieldOperand>().Distinct())
            {
                if (fld.Field.FieldType.IsPort()) continue;

                var fd = fld.Field.Resolve();

                var width = fld.OperandType.GetWidth();

                var resetValue = " = 0";
                var arrayLength = "";

                if (fd.CustomAttributes.Where(ca => ca.AttributeType.FullName == typeof(ResetValueAttribute).FullName).Any())
                    resetValue = " = " + fd.CustomAttributes.Where(ca => ca.AttributeType.FullName == typeof(ResetValueAttribute).FullName).First().ConstructorArguments[0].Value.ToString();

                if (fd.CustomAttributes.Where(ca => ca.AttributeType.FullName == typeof(ArrayLengthAttribute).FullName).Any())
                {
                    arrayLength = string.Format(" [0:{0}-1]", fd.CustomAttributes.Where(ca => ca.AttributeType.FullName == typeof(ArrayLengthAttribute).FullName).First().ConstructorArguments[0].Value.ToString());
                    resetValue = "";
                }

                sb.AppendLine($"    reg [{width - 1}:0] {fld.Field.Name}{arrayLength}{resetValue};");
            }

            // Define storage for results
            sb.AppendLine();
            foreach (var instr in method.AllInstructions())
            {
                if (instr.Result == 0) continue;

                string sign = instr.GetResultType(method).GetSigned() ? "signed " : "";
                int width = instr.GetResultType(method).GetWidth();
                sb.AppendLine($"    wire {sign}{getWidth(width)}res{instr.Result};");
            }

            // Create operands
            sb.AppendLine();
            foreach (var op in method.AllInstructions())
            {
                string res = "";
                string cond = "";
                bool clocked = false;

                switch (op.Op)
                {
                    case Op.Return: continue;

                    case Op.Slice:
                        var shift = Get(op[3]) != "0" ? " << " + op[3] : "";
                        res = string.Format("{0}[{1}:{2}]{3}", Get(op[0]), op[1], op[2], shift);
                        break;

                    case Op.Mov: res = string.Format("{0}", Get(op[0])); break;

                    case Op.Add: res = string.Format("{0} + {1}", Get(op[0]), Get(op[1])); break;
                    case Op.Sub: res = string.Format("{0} - {1}", Get(op[0]), Get(op[1])); break;

                    case Op.Lsl: res = string.Format("{0} << {1}", Get(op[0]), Get(op[1])); break;
                    case Op.Asr: res = string.Format("$signed({0}) >> {1}", Get(op[0]), Get(op[1])); break;
                    case Op.Lsr: res = string.Format("$unsigned({0}) >> {1}", Get(op[0]), Get(op[1])); break;

                    case Op.And: res = string.Join("&", op.Operands.Select(Get)); break;
                    case Op.Or: res = string.Join("|", op.Operands.Select(Get)); break;
                    case Op.Xor: res = string.Join("^", op.Operands.Select(Get)); break;

                    case Op.Mux: res = string.Format("{0} ? {2} : {1}", Get(op[0]), Get(op[1]), Get(op[2])); break;

                    case Op.LdFld: res = Get(op[1]); break;
                    case Op.LdArray:
                        sb.AppendLine(string.Format("always @(posedge Clock) res{0} <= {1}[{2}];", op.Result, Get(op[0]), Get(op[1])));
                        continue;
                        //res = Get(op[0]) + "[" + Get(op[1]) + "]";
                        //break;

                    case Op.StFld:
                        res = Get(op[1]) + " <= " + Get(op[2]);
                        clocked = true;
                        cond = Get(op[3]);
                        break;
                    case Op.StArray:
                        res = string.Format("{0}[{1}] <= {2}", Get(op[0]), Get(op[1]), Get(op[2]));
                        clocked = true;
                        cond = Get(op[3]);
                        break;

                    case Op.Select:
                        res = string.Join(":", op.Operands.Select(Get));
                        if (op.Operands.All(x => x is CondValue))
                            res += "0";
                        break;

                    case Op.InSet:
                        {
                            var src = Get(op[0]);
                            res = string.Join(" || ", op.Operands.Skip(1).Select(x => $"({src} == {Get(x)})"));
                            break;
                        }
                    case Op.NInSet:
                        {
                            var src = Get(op[0]);
                            res = string.Join(" && ", op.Operands.Skip(1).Select(x => $"({src} != {Get(x)})"));
                            break;
                        }
                    case Op.Insert:
                        {
                            var width = op.GetResultType(method).GetWidth();

                            var m = (int)(op[2] as ConstOperand).Value;
                            var l = (int)(op[3] as ConstOperand).Value;

                            var origin = Get(op[1]);

                            var v = Get(op[4]);

                            sb.AppendLine(string.Format("    wire [{0}:0] tmp{1} = {2};", width - 1, op.Result, origin));

                            if (l > 0)
                                sb.AppendLine(string.Format("    assign res{0}[{1}:0] = tmp{0}[{1}:0];", op.Result, l - 1));
                            if (m < width - 1)
                                sb.AppendLine(string.Format("    assign res{0}[{1}:{2}] = tmp{0}[{1}:{2}];", op.Result, width - 1, m + 1));
                            sb.AppendLine(string.Format("    assign res{0}[{1}:{2}] = {3};", op.Result, m, l, v));

                            continue;
                        }

                    case Op.Ceq: res = string.Format("{0} == {1}", Get(op[0]), Get(op[1])); break;
                    case Op.Clt: res = string.Format("$signed({0}) < $signed({1})", Get(op[0]), Get(op[1])); break;
                    case Op.Cltu: res = string.Format("$unsigned({0}) < $unsigned({1})", Get(op[0]), Get(op[1])); break;

                    case Op.WritePort:
                        {
                            var port = GetPorts(method, (op[0] as FieldOperand).Field.Resolve()).Where(x => !x.is_ready && !x.is_valid).ToList();

                            var outp = string.Join(",", port.Where(x => !x.input).Select(x => x.name).Reverse());

                            sb.AppendLine($"    assign {{{outp}}} = {Get(op[1])};");

                            continue;
                        }
                    case Op.ReadPort:
                        {
                            var port = GetPorts(method, (op[0] as FieldOperand).Field.Resolve()).Where(x => !x.is_ready && !x.is_valid).ToList();

                            var inp = string.Join(",", port.Where(x => x.input).Select(x => x.name).Reverse());

                            sb.AppendLine($"    assign res{op.Result} = {inp};");

                            continue;
                        }
                    case Op.Request:
                        {
                            var port = GetPorts(method, (op[0] as FieldOperand).Field.Resolve()).Where(x => !x.is_ready && !x.is_valid).ToList();

                            var outp = string.Join(",", port.Where(x => !x.input).Select(x => x.name).Reverse());
                            var inp = string.Join(",", port.Where(x => x.input).Select(x => x.name).Reverse());

                            sb.AppendLine($"    assign {{{outp}}} = {Get(op[1])};");
                            sb.AppendLine($"    assign res{op.Result} = {{{inp}}};");

                            continue;
                        }
                    case Op.Reg:
                        sb.AppendLine(string.Format("    always @(posedge Clock) res{0} <= {1};", op.Result, Get(op[0])));
                        continue;

                    default:
                        throw new Exception($"Unhandled opcode: {op.Op}");
                }

                if (op.Result != 0)
                    res = $"assign res{op.Result} = " + res;

                if (cond != "") res = $"if ({cond}) {res}";

                if (clocked)
                    res = "always @(posedge Clock) " + res;

                sb.AppendLine("    " + res + ";");
            }

            sb.AppendLine();
            sb.AppendLine("endmodule");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Settings.Filename)));
            File.WriteAllText(Settings.Filename, sb.ToString());
        }
    }
}