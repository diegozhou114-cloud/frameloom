using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZWebGLFrameWork.Server
{
    class Program
    {
        static Task<int> Main(string[] args)
        {
            return ReplayCli.RunAsync(args, Console.Out, Console.Error,
                CancellationToken.None);
        }
    }
}
