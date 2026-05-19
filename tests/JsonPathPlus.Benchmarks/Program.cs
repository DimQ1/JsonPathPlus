using BenchmarkDotNet.Running;

namespace JsonPathPlus.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        // Run all benchmarks if no arguments; otherwise pass args to BenchmarkSwitcher
        if (args.Length == 0)
        {
            BenchmarkRunner.Run(typeof(Program).Assembly);
        }
        else
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
