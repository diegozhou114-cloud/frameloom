using System;
using System.IO;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: ReplayValidator <replay.json>");
                return 1;
            }

            string filePath = args[0];
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"File not found: {filePath}");
                return 1;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                var replay = ServerReplayJsonCodec.Deserialize(json);

                var validator = new ReplayValidator(new ConsoleLogger());
                var result = validator.Validate(replay);

                Console.WriteLine($"Valid: {result.IsValid}");
                if (result.IsValid)
                {
                    Console.WriteLine($"Total ticks: {replay.TotalTicks}");
                }
                else
                {
                    Console.WriteLine($"Failed at tick: {result.FailedTick}");
                    Console.WriteLine($"Expected checksum: {result.ExpectedChecksum}");
                    Console.WriteLine($"Actual checksum:   {result.ActualChecksum}");
                }

                return result.IsValid ? 0 : 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }

    /// <summary>Console logger implementing ISimLogger for server-side use</summary>
    internal class ConsoleLogger : ISimLogger
    {
        public void Log(string message) => Console.WriteLine($"[LOG] {message}");
        public void Warn(string message) => Console.WriteLine($"[WARN] {message}");
        public void Error(string message) => Console.Error.WriteLine($"[ERROR] {message}");
    }
}
