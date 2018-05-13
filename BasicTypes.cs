using System;
using CilLogic.CodeModel;

namespace CilLogic
{
    public abstract class Actor
    {
        [Operation(Op.Sleep)]
        public void Sleep()
        {
        }

        public abstract void Execute();
    }

    public interface IPipe<T> where T : struct, IConvertible
    {

    }

    public interface IInput<T> : IPipe<T> where T : struct, IConvertible
    {
        [Operation(Op.ReadValid)]
        bool DataValid();
        [Operation(Op.ReadPort)]
        T ReadValue();
    }
    public interface IOutput<T> : IPipe<T> where T : struct, IConvertible
    {
        [Operation(Op.ReadReady)]
        bool CanWrite();
        [Operation(Op.WritePort)]
        void WriteValue(T data);
    }

    public class OperationAttribute : Attribute
    {
        public readonly Op Op;

        public OperationAttribute(Op op)
        {
            Op = op;
        }
    }

    public static class PipeHelpers
    {
        public static void Write<T>(this IOutput<T> output, T value, Actor actor) where T : struct, IConvertible
        {
            while (!output.CanWrite()) actor.Sleep();
            output.WriteValue(value);
        }
        
        public static T Read<T>(this IInput<T> output, Actor actor) where T : struct, IConvertible
        {
            while (!output.DataValid()) actor.Sleep();
            return output.ReadValue();
        }
    }
}