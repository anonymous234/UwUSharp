using System.CommandLine;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;



namespace UwUSharp
{
    internal class Program
    {
        //Main2 is the real entry point. This method exists solely because System.CommandLine.DragonFruit breaks NativeAOT and it doesn't seem like it will get fixed:
        public static void Main(string[] args)
        {
            var inputOption = new Option<string?>(
                name: "--input",
                description: "Input text file.");
            var outputOption = new Option<string?>(
                name: "--output",
                description: "Output text file.");
            var threadsOption = new Option<int?>(
                name: "--threads",
                description: "Number of threads to use.");
            var benchmarkOption = new Option<bool>(
                name: "--measure",
                description: "This will show the total run time and throughput, and will also run a second \"uwuify[.exe]\" executable if it exists");
            var rootCommand = new RootCommand();
            rootCommand.AddOption(inputOption);
            rootCommand.AddOption(outputOption);
            rootCommand.AddOption(threadsOption);
            rootCommand.AddOption(benchmarkOption);
            var parsed = rootCommand.Parse(args);
            Main2(parsed.GetValueForOption(inputOption),
                  parsed.GetValueForOption(outputOption),
                  parsed.GetValueForOption(threadsOption),
                  parsed.GetValueForOption(benchmarkOption));

        }

        /// <param name="input">Input text file</param>
        /// <param name="output">Output text file</param>
        /// <param name="measure">This will show the total run time and throughput, and will also run a second "uwuify[.exe]" executable if it exists</param>
        /// <param name="threads">Number of threads</param>
        private static void Main2(string? input, string? output = null, int? threads = null, bool measure = false)
        {
            //We include initialization time. To measure just the main code use UwUSharp.Bench
            var start_time = Stopwatch.StartNew();
            RuntimeHelpers.RunClassConstructor(typeof(Lib).TypeHandle);
            RuntimeHelpers.RunClassConstructor(typeof(BitapConstants).TypeHandle);

            if (input == output && input != null) throw new Exception("Input and output cannot be the same file");
            //We don't use TextReader because that would force converting to and from UTF-16. We keep it binary all the way. The code assumes it's UTF-8.
            using Stream reader = input == null ? Console.OpenStandardInput() : File.OpenRead(input);
            using Stream writer = output == null ? Console.OpenStandardOutput() : File.OpenWrite(output);
            threads ??= Environment.ProcessorCount;

            if (!Vector128.IsHardwareAccelerated)
                Console.WriteLine("Warning: Vector128 is not hardware accelerated");
            else if (!Sse2.IsSupported && !AdvSimd.IsSupported && !System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported)
                Console.WriteLine("Warning: ShiftLogical128Bit is not hardware accelerated");

            var (input_size, output_size) = Lib.StreamUwu(reader, writer, threads.Value);
            var duration = start_time.Elapsed;

            if (measure)
            {
                //These admittedly don't take into account initialization time...
                Console.Error.WriteLine($"Threads = {threads}");
                Console.Error.WriteLine($"Size: {input_size} bytes input, {output_size} bytes output");
                Console.Error.WriteLine($"Time taken: {duration.TotalMilliseconds} ms");
                Console.Error.WriteLine($"Throughput: {input_size / ((float)duration.TotalNanoseconds):F5} gb/s");


                //Run the uwuify executable if it exists
                var rust_uwu = OperatingSystem.IsWindows() ? "uwuify.exe" : "uwuify";
                if (File.Exists(rust_uwu) && input != null && output != null)
                {
                    Console.Error.WriteLine($"\nRunning uwuify.exe:");
                    var p = Process.Start(new ProcessStartInfo
                    {
                        FileName = rust_uwu,
                        Arguments = $"\"{input}\" \"{output}2\" -t {threads} -v",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });
                    if (p == null) Console.Error.WriteLine("Failed to start uwuify.exe");
                    else
                    {
                        string s = p.StandardError.ReadToEnd();
                        s = s.ReplaceLineEndings(); //Seems to be a problem...
                        p.WaitForExit();
                        Console.Write(s);
                    }
                }
            }
        }

    }
}