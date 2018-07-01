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
            public bool signed, input;
        }

        public VerilogSettings Settings = new VerilogSettings();

        private IEnumerable<PortDefinition> GetPorts(Method method, TypeDefinition arg, bool inDir, string prefix)
        {
            if (arg.IsSimple())
                yield return new PortDefinition { name = prefix + "data", input = !inDir, signed = arg.GetSign(method), width = arg.GetWidth(method) };
            else
            {
                foreach (var portField in arg.Fields)
                    yield return new PortDefinition { name = prefix + portField.Name, input = false, signed = portField.FieldType.GetSign(method, arg), width = portField.FieldType.GetWidth(method, arg) };
            }

            yield return new PortDefinition { name = prefix + "valid", input = !inDir, signed = false, width = 1 };
            yield return new PortDefinition { name = prefix + "ready", input = inDir, signed = false, width = 1 };
        }

        private IEnumerable<PortDefinition> GetPorts(Method method, FieldDefinition field)
        {
            var prefix = field.Name + "_";

            if (field.CustomAttributes.Any(o => o.AttributeType.Name == "PortPrefixAttribute"))
            {
                var val = (string)field.CustomAttributes[0].ConstructorArguments[0].Value;
                prefix = val + "_";
            }

            var ft = field.FieldType.Resolve(field.FieldType, method.GenericParams);

            if (ft.IsRequestPort())
            {
                var key = ft.GenericParameters.First().Resolve(field.FieldType, method.GenericParams);
                var value = ft.GenericParameters.Last().Resolve(field.FieldType, method.GenericParams);

                foreach (var o in GetPorts(method, key, false, prefix + "o")) yield return o;
                foreach (var o in GetPorts(method, value, false, prefix + "i")) yield return o;
            }
            else
            {
                var key = ft.GenericParameters.First().Resolve(field.FieldType, method.GenericParams);

                foreach (var o in GetPorts(method, key, ft.IsInPort(), prefix)) yield return o;
            }
        }

        private IEnumerable<string> GetPorts(Method method)
        {
            yield return "Clock";

            var type = method.MethodRef.DeclaringType.Resolve();

            foreach (var x in type.Fields.Where(x => x.FieldType.Resolve().IsPort()).SelectMany(p => GetPorts(method, p)).Select(r =>
            {
                var dir = r.input ? "input" : "output";
                var width = r.width > 1 ? $"[{r.width - 1}:0}}] " : "";
                var reg = r.input ? "" : "reg ";

                return $"{dir} {reg}{width}{r.name}";
            }))
                yield return x;
        }

        public override void Pass(Method method)
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine(string.Format("module {0}(", method.MethodRef.DeclaringType.Name));
            sb.AppendLine(string.Join("," + Environment.NewLine, GetPorts(method).Select(x => "    " + x)));
            sb.AppendLine(");");


            sb.AppendLine("endmodule");

            File.WriteAllText(Settings.Filename, sb.ToString());
        }
    }
}