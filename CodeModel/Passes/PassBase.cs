using System;

namespace CilLogic.CodeModel.Passes
{
    public abstract class CodePass
    {
        public abstract void Pass(Method method);

        public static void Process(Method m, bool ssa = true)
        {
            new PassDeadCode().Pass(m);
            new PassPeephole().Pass(m);
            new PassDeadCode().Pass(m);

            new PassDeadCode().Pass(m);
            
            new InlinePass().Pass(m);

            new PassDeadCode().Pass(m);

            if (ssa) new SsaPass().Pass(m);
            
            for(int i=0; i<20; i++)
            {
                new PassPeephole().Pass(m);
                new PassDeadCode().Pass(m);
            }
        }
    }
}