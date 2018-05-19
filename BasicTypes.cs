using System;
using CilLogic.CodeModel;

namespace CilLogic.Types
{
    public abstract class Actor
    {
        [Operation(Op.Stall)]
        public void Stall(bool condition = true)
        {
        }

        public abstract void Execute();
    }

    public interface IPipe<T> where T : struct
    {

    }

    public interface IInput<T> : IPipe<T> where T : struct
    {
        [Operation(Op.ReadValid)]
        bool DataValid();
        [Operation(Op.ReadPort)]
        T ReadValue();
    }
    public interface IOutput<T> : IPipe<T> where T : struct
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
        public static void Write<T>(this IOutput<T> output, T value, Actor actor) where T : struct
        {
            while (!output.CanWrite()) actor.Stall();
            output.WriteValue(value);
        }
        
        public static T Read<T>(this IInput<T> output, Actor actor) where T : struct
        {
            while (!output.DataValid()) actor.Stall();
            return output.ReadValue();
        }
    }

    public class BitWidthAttribute : Attribute
    {
        public int Width { get; }

        public BitWidthAttribute(int width)
        {
            Width = width;
        }
    }
}