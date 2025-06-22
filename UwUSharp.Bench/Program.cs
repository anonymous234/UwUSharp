using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using System.IO;
using UwUSharp;

BenchmarkRunner.Run<UwUBenchmark>();



/// <summary>
/// UwUifies a text file in-memory, reports the results with BenchmarkDotNet
/// </summary>
[Config(typeof(Config))]
[SimpleJob(RuntimeMoniker.Net90, baseline: true)]
//[SimpleJob(RuntimeMoniker.NativeAot90)] //build errors
public class UwUBenchmark : IDisposable
{
    private class ThroughputColumn(double inputSizeBytes) : IColumn
    {
        public string Id => nameof(TagColumn) + "." + ColumnName;
        public string ColumnName => "GB/s";
        public bool IsAvailable(Summary summary) => true;
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Custom;
        public int PriorityInCategory => 0;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Size;
        public string Legend => $"Gigabytes/s";
        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
        public override string ToString() => ColumnName;

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
        {
            var meanNs = summary[benchmarkCase]!.ResultStatistics!.Mean;
            // mean is in nanoseconds
            double GBps = inputSizeBytes / meanNs;
            return GBps.ToString("F3"); // Format with 3 decimal places
        }
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    }
    private class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(new ThroughputColumn(3359652.0));
            AddColumn(new ThroughputColumn(new System.IO.FileInfo(@"pg2600.txt").Length));
        }
    }
    private MemoryStream? input;
    private MemoryStream? output;

    [Params(1, 2, 6, 12, 24)]
    public int Threads;

    [GlobalSetup]
    public void Setup()
    {
        input = new MemoryStream();
        output = new MemoryStream();


        using var file = File.OpenRead(@"pg2600.txt");
        file.CopyTo(input);
    }
    [Benchmark]
    public void UwuifyFile()
    {
        input!.Position = 0;
        output!.Position = 0;
        if (Threads == 1) Lib.StreamUwu(input, output);
        else Lib.StreamUwu(input, output, Threads);
    }
    public void Dispose()
    {
    }
}