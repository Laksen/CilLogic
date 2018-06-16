using Mono.Cecil;
using CilLogic.Utilities;
using System;

namespace CilLogic.CodeModel
{
    public class TypeDef : IComparable
    {
        public static TypeDef Void => new TypeDef();
        public static TypeDef Unknown => new TypeDef();

        public int CompareTo(object obj)
        {
            if (obj is TypeDef td)
            {
                var aw = GetWidth();
                var bw = td.GetWidth();

                return
                    (aw > bw) ? 1 :
                    (aw < bw) ? -1 :
                    0;
            }
            else
                return 0;
        }

        public virtual int GetWidth()
        {
            return 0;
        }

        public virtual bool GetSigned()
        {
            return false;
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

        public override bool GetSigned()
        {
            return Signed;
        }

        public static VectorType Int64 = new VectorType(64, true);
        public static VectorType Int32 = new VectorType(32, true);

        public static TypeDef UInt1 = new VectorType(1, false);

        public int Width { get; }
        public bool Signed { get; }
    }

    public class CecilType<T> : TypeDef where T : MemberReference
    {
        public MemberReference MemberRef { get; }

        public readonly Method Method;

        public CecilType(T td, Method method, MemberReference mRef = null)
        {
            MemberRef = mRef;
            Method = method;
            Type = td;
        }

        public override bool GetSigned()
        {
            var td = Type as TypeReference;

            if (td != null)
                return td.GetSign(Method, MemberRef);
            else
                return false;
        }

        public override int GetWidth()
        {
            var td = Type as TypeReference;

            if (td != null)
                return td.GetWidth(Method, MemberRef);
            else
                return 0;
        }

        public T Type { get; }
    }
}