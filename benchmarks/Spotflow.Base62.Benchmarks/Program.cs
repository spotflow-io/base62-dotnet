using BenchmarkDotNet.Running;

using Spotflow.Base62.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(Base62Benchmarks).Assembly).Run(args);
