using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CilLogic.Utilities;
using Mono.Cecil;

namespace CilLogic.CodeModel.Passes
{
    public class VerilogSettings
    {
        public string Filename = "out.v";
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
                    yield return new PortDefinition { name = prefix + portField.Name, input = inDir, registered = false, signed = portField.FieldType.GetSign(method, arg), width = portField.FieldType.GetWidth(method, arg) };
            }

            yield return new PortDefinition { name = prefix + "valid", input =  inDir, registered = false, signed = false, width = 1, is_valid = true };
            yield return new PortDefinition { name = prefix + "ready", input = !inDir, registered = false, signed = false, width = 1, is_ready = true };
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
                var width = r.width > 1 ? $"[{r.width - 1}:0}}] " : "";
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

                sb.AppendLine(string.Format(" field: {0}", fld));
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

                switch (op.Op)
                {
                    case Op.Return: break;

                    case Op.Slice:
                        res = string.Format("{0}[{1}:{2}] << {3}", Get(op[0]), op[1], op[2], op[3]);
                        break;

                    case Op.Add: res = string.Format("{0} + {1}", Get(op[0]), Get(op[1])); break;
                    case Op.Sub: res = string.Format("{0} + {1}", Get(op[0]), Get(op[1])); break;

                    case Op.And: res = string.Join("&", op.Operands.Select(Get)); break;
                    case Op.Or: res = string.Join("|", op.Operands.Select(Get)); break;
                    case Op.Xor: res = string.Join("^", op.Operands.Select(Get)); break;

                    case Op.Mux: res = string.Format("{0} ? {1} : {2}", Get(op[0]), Get(op[1]), Get(op[2])); break;

                    case Op.LdFld: res = Get(op[1]); break;
                    case Op.StFld: res = Get(op[1]) + " <= " + Get(op[2]); cond = Get(op[3]); break;

                    case Op.LdArray: res = Get(op[0]) + "[" + Get(op[1]) + "]"; break;
                    case Op.StArray: res = string.Format("{0}[{1}] <= {2}", op[0], op[1], op[2]); cond = Get(op[3]); break;

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

                    case Op.Clt: res = string.Format("$signed({0}) < $signed({1})", Get(op[0]), Get(op[1])); break;
                    case Op.Cltu: res = string.Format("$unsigned({0}) < $unsigned({1})", Get(op[0]), Get(op[1])); break;

                    case Op.Request:
                        {
                            var port = GetPorts(method, (op[0] as FieldOperand).Field.Resolve());

                            //sb.AppendLine("    assign {}")

                            break;
                        }

                    default:
                        throw new Exception($"Unhandled opcode: {op.Op}");
                }

                if (op.Result != 0)
                    res = $"assign res{op.Result} = " + res;

                if (cond != "") res = $"if ({cond}) {res}";

                sb.AppendLine("    " + res + ";");
            }

            sb.AppendLine();
            sb.AppendLine("endmodule");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Settings.Filename)));
            File.WriteAllText(Settings.Filename, sb.ToString());
        }
    }
}