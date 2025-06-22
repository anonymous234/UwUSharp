## UwUSharp - SIMD C# text uwuifier

The fastest JIT-compiled uwuifier in the west

UwUSharp is a high-performance, memory-safe C# port of the Rust-based uwuifier: https://github.com/Daniel-Liu-c0deb0t/uwu

### Why?

.NET supports portable, SIMD-accelerated code. I wanted to show that C# code can be efficient and that managed code is still cool.

Unfortunately it only ended up being 50% to 70% as fast as the Rust version, but hey, that’s pretty good.

### Design principles:

- Straightforward translation of the original code using [Vector128](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.vector128?view=net-9.0)
- Follows C# conventions for things like naming, testing, etc.
- Memory safe code with basic optimizations (such as using Span<byte> to avoid copying buffers).
- Maintain identical output to the original project for easy validation

### Features

- Uses `Span<byte>` and avoids unnecessary allocations
- Multi-threaded support using TPL Dataflow
- Usable as a library or as a standalone program
- Cross-platform: works on any OS and processor that .NET supports, (including x86, ARM, WebAssembly) (untested)
- Unit tests and performance measurement using BenchmarkDotNet
- Identical output to the reference implementation
- Can automatically run the original Rust implementation with the same parameters to compare performance

### Benchmarks

Sample results using .NET 9, on a Ryzen 7600 6-core processor with 6000 MT/s RAM:

|  | GB/s (1 thread) | GB/s (6 threads) |
| --- | --- | --- |
| Rust uwuify | 0.615 | 2.37 |
| .NET, inner loop only (BenchmarkDotNet) | 0.349 | 1.633 |
| .NET (NativeAOT compiled) | 0.296 | 1.232 |
| .NET (JIT, disable tiered compilation) | 0.343 | 1.181 |
| .NET (JIT, cold start, default settings) | 0.217 | 0.828 |
| .NET (JIT, Debug build) | 0.020 | 0.106 |

### Problems encountered:

- Vector128<byte> does not have a way to shift the entire vector by one byte (_mm_slli_si128), so I had to implement it using explicit SSE2, ARM Neon and WASM instructions, with a fallback to standard byte arrays.
- .NET's default execution uses "tiered compilation", which generates un-optimized code at launch, and only optimizes the code after it's been running for some time (longer than it will actually take to process a file). This made the code run significantly slower. Thankfully it can be easily disabled in the project file. Note that the code still appears to get slightly faster after a few loops.
- No “const” variables, meaning no compile-time lookup tables, which are required for the bitap algorithm. Instead, they are computed on every startup.
- No easy way to declare a fixed-length array. C# has "fixed-size buffers" which are unsafe, and [InlineArray] which has awkward syntax and is not well documented.
- C# uses UTF-16 for strings, which means I can't use string methods since it would force a conversion.
- There is no way to define objects as memory-aligned. I believe .NET will align an array of Vector128<byte>anyway, but I couldn't find a confirmation. I wrote a version using manually allocated arrays and aligned reads, but it did not seem to be measurably faster.

### How to run 

- Install .NET 9 SDK

```bash
cd UwUSharp
dotnet build -c Release
dotnet UwUSharp\bin\Release\net9.0\UwUSharp.dll --input file.txt --output file_out.txt --threads 1 --measure
```

Or if you prefer NativeAOT:

```bash
dotnet publish .\UwUSharp\ -r win-x64 -c Release
.\UwUSharp\bin\Release\net9.0\win-x64\publish\UwUSharp.exe --input file.txt --output file_out.txt --threads 1 --measure
```

If using Visual Studio, remember that the running with the debugger makes it significantly slower!

Optionally, you can build the original Rust project,  with `cargo build --release`, then copy the single executable produced in the current folder. This will automatically run it with the same parameters when running the C# version.