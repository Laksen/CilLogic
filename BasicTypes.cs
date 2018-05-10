using System;

namespace CilLogic
{
    public abstract class Actor
    {
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
        bool DataValid();
        bool TryRead(out T data);
    }
    public interface IOutput<T> : IPipe<T> where T : struct, IConvertible
    {
        bool CanWrite();
        bool TryWrite(T data);
    }

    public static class PipeHelpers
    {
        public static void Write<T>(this IOutput<T> output, T value, Actor actor) where T : struct, IConvertible
        {
            while (!output.TryWrite(value)) actor.Sleep();
        }
        
        public static T Read<T>(this IInput<T> output, Actor actor) where T : struct, IConvertible
        {
            T result;
            while (!output.TryRead(out result)) actor.Sleep();
            return result;
        }
    }
}