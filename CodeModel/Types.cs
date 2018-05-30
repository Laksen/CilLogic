using Mono.Cecil;
using CilLogic.Utilities;

namespace CilLogic.CodeModel
{
    public class TypeDef
    {
        public static TypeDef Void = new TypeDef();
        public static TypeDef Unknown = new TypeDef();

        public virtual int GetWidth()
        {
            return 0;
        }

        public static implicit operator TypeDef(TypeDefinition type) { return new CecilType<TypeDefinition>(type, null); }
    }

    public class VectorType : TypeDef
    {
        public VectorType(int width, bool signed)
        {
            Width = width;
            Signed = signed;
        }

        public override int GetWidth()
        {
            return Width;
        }

        public static VectorType Int64 = new VectorType(64, true);
        public static VectorType Int32 = new VectorType(32, true);
        
        public static TypeDef UInt1 = new VectorType(1, false);

        public int Width { get; }
        public bool Signed { get; }
    }

    public class CecilType<T> : TypeDef where T : MemberReference
    {
        public readonly Method Method;

        public CecilType(T td, Method method)
        {
            Method = method;
            Type = td;
        }

        public override int GetWidth()
        {
            var td = Type as TypeReference;

            if (td != null)
                return td.GetWidth(Method);
            else
                return 0;
        }

        public T Type { get; }
    }
}