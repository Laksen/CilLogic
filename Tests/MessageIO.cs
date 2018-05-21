using System;
using CilLogic;
using CilLogic.Types;

namespace CilLogic.Tests
{
    public enum InType
    {
        Load,
        Accumulate,
        Propagate
    }

    public struct InMessage
    {
        [BitWidth(2)]
        public InType Type;
        [BitWidth(32)]
        public UInt32 Operand;
    }

    public struct OutMessage
    {
        public UInt32 Count;
        public UInt32 Sum;
    }

    public class MessageIO : Actor
    {
        public IInput<InMessage> InPort { get; set; }
        public IOutput<OutMessage> OutPort { get; set; }

        private UInt32 accumulator = 0;
        private UInt32 count = 0;

        public override void Execute()
        {
            var msg = InPort.Read(this);

            UInt32 newAcc = accumulator;

            switch (msg.Type)
            {
                case InType.Load:
                    newAcc = msg.Operand;
                    break;
                case InType.Accumulate:
                    newAcc += msg.Operand;
                    count++;
                    break;
                case InType.Propagate:
                    OutPort.Write(new OutMessage { Sum = accumulator, Count = count }, this);
                    break;
            }

            accumulator = newAcc;
        }
    }
}