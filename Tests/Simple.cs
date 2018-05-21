using CilLogic.Types;

namespace CilLogic.Tests
{
    public class SimpleActor : Actor
    {
        public IInput<int> a { get; set; }
        public IOutput<int> b { get; set; }

        public int test;

        public override void Execute()
        {
            int x = test;

            x = (x > 2) ? 44 : 2;

            test = x;
            //b.Write(a.Read(this), this);
        }
    }
}