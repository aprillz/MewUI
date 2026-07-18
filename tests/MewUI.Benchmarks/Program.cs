using BenchmarkDotNet.Running;

// Run every benchmark, or filter from the command line, e.g.:
//   dotnet run -c Release -- --filter *PropertyStore*
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal sealed partial class Program;
