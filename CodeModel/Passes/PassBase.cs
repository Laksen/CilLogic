using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CilLogic.CodeModel.Passes
{
    public abstract class CodePass
    {
        public abstract void Pass(Method method);

        public static Dictionary<string, TimeSpan> PassTime = new Dictionary<string, TimeSpan>();

        protected static void DoPass<T>(Method m, string tag = "") where T : CodePass, new()
        {
            Stopwatch s = Stopwatch.StartNew();
            new T().Pass(m);
            s.Stop();

            var name = tag + typeof(T).Name;

            if (!PassTime.ContainsKey(name))
                PassTime[name] = new TimeSpan(0);

            PassTime[name] += s.Elapsed;
        }

        public static void Process(Method m, bool ssa = true)
        {
            DoPass<PassDeadCode>(m);
            DoPass<PassPeephole>(m);
            DoPass<PassDeadCode>(m);

            DoPass<PassDeadCode>(m);

            DoPass<InlinePass>(m);

            DoPass<PassDeadCode>(m);

            if (ssa) DoPass<SsaPass>(m);

            for (int i = 0; i < 20; i++)
            {
                DoPass<PassPeephole>(m);
                DoPass<PassDeadCode>(m);
            }
        }
    }
}