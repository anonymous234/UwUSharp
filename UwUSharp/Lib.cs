using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading.Tasks.Dataflow;
using Vec = System.Runtime.Intrinsics.Vector128<byte>;

[assembly: InternalsVisibleTo("UwUSharp.Tests")]

namespace UwUSharp
{
    /// <summary>
    /// Second fastest text uwuifier in the west
    /// </summary>
    public static class Lib
    {
        // should be small enough so stuff fits in L1/L2 cache
        // but big enough so each thread has enough work to do
        // Changing buffer size affects the result
        const int BUFFER_LEN = 1 << 16; //64 KB


        /// <summary>
        /// Round up <paramref name="n"/> to the next multiple of 16. useful for allocating buffers
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int RoundUp16(int n)
        {
            return (n + 15) / 16 * 16;
        }
        /// <summary>
        /// Pads from [len] to the end with zeros
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PadZeros(Span<byte> bytes, int len)
        {
            //pad with zeros
            for (int i = len; i < RoundUp16(len); i++) bytes[i] = 0;
        }

        ///<summary>
        /// Uwuify a string slice
        /// This is probably fine for one-off use, but not very efficient if called multiple times.
        /// use <see cref="Uwuify(ReadOnlySpan{byte}, Span{byte}, Span{byte})"/> to reduce memory allocations.
        ///</summary>
        public static string Uwuify(string s)
        {
            return Encoding.UTF8.GetString(Uwuify(Encoding.UTF8.GetBytes(s)));
        }
        /// <summary>
        /// uwuify a byte slice containing UTF-8 text
        /// If called multiple times, use <see cref="Uwuify(ReadOnlySpan{byte}, Span{byte}, Span{byte})"/> to reduce memory allocations.
        /// </summary>
        public static byte[] Uwuify(ReadOnlySpan<byte> bytes)
        {
            var temp1 = ArrayPool<byte>.Shared.Rent(RoundUp16(bytes.Length) * 16);
            var temp2 = ArrayPool<byte>.Shared.Rent(RoundUp16(bytes.Length) * 16);
            var result = Uwuify(bytes, temp1, temp2).ToArray();
            ArrayPool<byte>.Shared.Return(temp1);
            ArrayPool<byte>.Shared.Return(temp2);
            return result;
        }

        ///<summary>
        /// <para>uwuify some bytes (UTF-8).</para>
        /// <para>
        /// <paramref name="temp_bytes1"/> and <paramref name="temp_bytes2"/> must be buffers of size <c>RoundUp16(bytes.Length) * 16</c>,
        /// because this is the worst-case size of the output. yes, it is annoying to allocate by
        /// hand, but simd :)</para>
        /// <para>The returned Span will point to the same buffer as <paramref name="temp_bytes2"/></para>
        /// </summary>
        /// <returns>The returned slice as the uwu'd result. when working with utf-8 strings, just pass in
        /// the string as raw bytes and convert the output slice back to a string afterwards.
        /// there's also the <see cref="Uwuify(string)"/> function that is suitable for one-off use with a string</returns>
        public static Span<byte> Uwuify(ReadOnlySpan<byte> bytes, Span<byte> temp_bytes1, Span<byte> temp_bytes2)
        {    
            if (temp_bytes1.Length < RoundUp16(bytes.Length) * 16
                || temp_bytes2.Length < RoundUp16(bytes.Length) * 16)
            {
                throw new Exception();
            }

            // only the highest quality seed will do
            var rng = new XorShift32("uwu!"u8);

            // bitap_sse will not read past len, unlike the other passes
            var len = BitapSse(bytes, temp_bytes1);
            PadZeros(temp_bytes1, len);
            len = NyaIfySse(temp_bytes1, len, temp_bytes2);
            PadZeros(temp_bytes2, len);
            len = ReplaceAndStutterSse(rng, temp_bytes2, len, temp_bytes1);
            PadZeros(temp_bytes1, len);
            len = EmojiSse(rng, temp_bytes1, len, temp_bytes2);
            return temp_bytes2[..len];

        }

        /// <summary>
        /// UwUfies input stream and writes it to output stream (single threaded)
        /// </summary>
        public static (int input_size, int output_size) StreamUwu(Stream reader, Stream writer)
        {
            int input_bytes = 0, output_bytes = 0;
            var buffer = new byte[BUFFER_LEN];
            var temp1 = new byte[RoundUp16(BUFFER_LEN) * 16];
            var temp2 = new byte[RoundUp16(BUFFER_LEN) * 16];
            while (true)
            {
                var read = reader.Read(buffer);
                if (read == 0) break;
                input_bytes += read;
                var res = Uwuify(buffer.AsSpan(0, read), temp1, temp2);
                writer.Write(res);
                output_bytes += res.Length;
            }
            return (input_bytes, output_bytes);
        }

        /// <summary>
        /// UwUfies streams in parallel
        /// </summary>
        public static (int input_size, int output_size) StreamUwu(Stream reader, Stream writer, int threads)
        {
            if (threads == 1) return StreamUwu(reader, writer);
            //Processing chunks in parallel and writing them in order is not trivial
            //The original code used signals to synchronize each thread to write in order
            //Luckily Dataflow does that for us (hopefully efficiently enough)
            //This method is still deterministic since the random number generator is seeded for each chunk

            int input_bytes = 0, output_bytes = 0;
            TransformBlock<byte[], byte[]> block1 = new(
                b => Lib.Uwuify(b.AsSpan()),
                new ExecutionDataflowBlockOptions
                {
                    SingleProducerConstrained = true,
                    MaxDegreeOfParallelism = threads,
                    EnsureOrdered = true,
                    BoundedCapacity = 1000
                });

            ActionBlock<byte[]> blockWrite = new(bytes =>
            {
                writer.Write(bytes);
                output_bytes += bytes.Length;
            },
                new ExecutionDataflowBlockOptions
                {
                    SingleProducerConstrained = true,
                    MaxDegreeOfParallelism = 1,
                    BoundedCapacity = 1000
                });
            block1.LinkTo(blockWrite, new DataflowLinkOptions { PropagateCompletion = true });

            while (true)
            {
                var buffer = new byte[BUFFER_LEN];
                var read = reader.Read(buffer);
                if (read == 0) break;
                else if (read < BUFFER_LEN)
                    //should generally only happen once
                    buffer = buffer[..read];
                input_bytes += read;
                bool b = block1.SendAsync(buffer).Result;
                if (!b)
                {
                    Console.WriteLine("Error in TransformBlock");
                    //continue to catch the exception at the end
                    break;
                }
            }
            ;
            block1.Complete();
            blockWrite.Completion.Wait();

            return (input_bytes, output_bytes);
        }

        /// <summary>
        /// Performs replacements in Bitap
        /// </summary>
        ///<returns>Length of valid string in out_bytes</returns>
        static int BitapSse(ReadOnlySpan<byte> in_bytes, Span<byte> out_bytes)
        {
            //We are using SIMD to store one value (hopefully it's faster than Array.Copy)
            var bitap = new Bitap.Bitap8x16();
            int out_bytes_ptr = 0;
            foreach (byte c in in_bytes)
            {
                out_bytes[out_bytes_ptr] = c;
                out_bytes_ptr++;
                //Skip HasValue check since we know length can't be 0
                var m = bitap.Next(c).GetValueOrDefault();
                if (m.MatchLen > 0)
                {
                    out_bytes_ptr -= m.MatchLen;
                    m.Replace.CopyTo(out_bytes.Slice(out_bytes_ptr,16));
                    out_bytes_ptr += m.ReplaceLen;
                    bitap.Reset();
                }
            }
            return out_bytes_ptr;
        }

        static readonly byte[] buffer = new byte[16];
        //Stores string in Vec assuming length < 16.
        internal static Vec StrToBytes(string s)
        {
            Array.Clear(buffer);
            Encoding.UTF8.GetBytes(s, buffer);
            Vec res = Vector128.Create(buffer);
            return res;
        }
        // this lookup table needs to be power of two sized
        readonly static Vec[] LUT = new string[]{
          " rawr x3",
          " OwO",
          " UwU",
          " o.O",
          " -.-",
          " >w<",
          " (⑅˘꒳˘)",
          " (ꈍᴗꈍ)",
          " (˘ω˘)",
          " (U ᵕ U❁)",
          " σωσ",
          " òωó",
          " (///ˬ///✿)",
          " (U ﹏ U)",
          " ( ͡o ω ͡o )",
          " ʘwʘ",
          " :3",
          " :3", // important enough to have twice
          " XD",
          " nyaa~~",
          " mya",
          " >_<",
          " 😳",
          " 🥺",
          " 😳😳😳",
          " rawr",
          " ^^",
          " ^^;;",
          " (ˆ ﻌ ˆ)♡",
          " ^•ﻌ•^",
          " /(^•ω•^)",
          " (✿oωo)",
        }.Select(StrToBytes).ToArray();
        static readonly int[] LUT_LEN = GetLen(LUT);
        internal static int[] GetLen(Vec[] a)
        {
            static int BytesLen(Vec chunk)
            {
                for (int i = 0; i < 16; i++)
                {
                    if (chunk[i] == 0) return i;
                }
                return 16;
            }
            var res = new int[LUT.Length];
            var i = 0;

            while (i < a.Length)
            {
                res[i] = BytesLen(a[i]);
                i++;
            }
            return res;
        }
        static int EmojiSse(
            XorShift32 rng,
            ReadOnlySpan<byte> in_bytes,
            int len,
            Span<byte> out_bytes)
        {
            //var in_bytes_ptr = 0;
            var out_bytes_ptr = 0;

            var splat_period = Vector128.Create<byte>((byte)'.');
            var splat_comma = Vector128.Create<byte>((byte)',');
            var splat_exclamation = Vector128.Create<byte>((byte)'!');
            var splat_space = Vector128.Create<byte>((byte)' ');
            var splat_tab = Vector128.Create<byte>((byte)'\t');
            var splat_newline = Vector128.Create<byte>((byte)'\n');
            var indexes = Vector128.Create((byte)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);

            int lut_bits = BitOperations.TrailingZeroCount(LUT.Length); //5;

            var iter_len = RoundUp16(len);

            for (int i = 0; i < iter_len; i += 16)
            {
                var vec = Vector128.Create(in_bytes[i..]);

                var punctuation_mask = Vector128.Equals(vec, splat_comma) | (Vector128.Equals(vec, splat_period) | Vector128.Equals(vec, splat_exclamation));
                // multiple punctuation in a row means no emoji
                var multiple_mask = punctuation_mask & punctuation_mask.ShiftLeftLogical128BitLane();
                multiple_mask |= multiple_mask.ShiftRightLogical128BitLane();

                // punctuation must be followed by a space or else no emoji
                var space_mask = Vector128.Equals(vec, splat_space) | Vector128.Equals(vec, splat_tab) | Vector128.Equals(vec, splat_newline);
                punctuation_mask = punctuation_mask & space_mask.ShiftRightLogical128BitLane() & ~multiple_mask;
                vec.CopyTo(out_bytes.Slice(out_bytes_ptr, 16));
                uint insert_mask = Vector128.ExtractMostSignificantBits(punctuation_mask);

                // be lazy and only allow one emoji per vector
                if (insert_mask != 0)
                {
                    var insert_idx = BitOperations.TrailingZeroCount(insert_mask) + 1;
                    var rand_idx = rng.GenBits(lut_bits);
                    var insert_vec = LUT[rand_idx];
                    var insert_len = LUT_LEN[rand_idx];

                    insert_vec.CopyTo(out_bytes.Slice(out_bytes_ptr + insert_idx, 16));
                    // shuffle to shift right by amount only known at runtime
                    //TODO: Change shuffle to shift?
                    var rest_vec = Vector128.Shuffle(vec, indexes + Vector128.Create((byte)insert_idx));

                    rest_vec.CopyTo(out_bytes.Slice(out_bytes_ptr + insert_idx + insert_len, 16));
                    out_bytes_ptr += insert_len;
                    len += insert_len;
                }

                out_bytes_ptr += 16;
            }

            return len;
        }
        /// <summary>
        /// Converts " n" to " ny"
        /// </summary>
        /// <param name="in_bytes"></param>
        /// <param name="len"></param>
        /// <param name="out_bytes"></param>
        /// <returns></returns>
        static int NyaIfySse(ReadOnlySpan<byte> in_bytes, int len, Span<byte> out_bytes)
        {
            var out_bytes_ptr = 0;
            //16 bytes, repeated
            var bit5 = Vector128.Create((byte)0b0010_0000);
            var splat_n = Vector128.Create((byte)'n');
            var splat_space = Vector128.Create((byte)' ');
            var splat_tab = Vector128.Create((byte)'\t');
            var splat_newline = Vector128.Create((byte)'\n');
            var indexes = Vector128.Create((byte)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);

            var iter_len = RoundUp16(len);

            for (int i = 0; i < iter_len; i += 16)
            {
                //round up to blocks of 16
                //for each i where i is the beginning of the block:

                //Read the input
                var vec = Vector128.Create(in_bytes[i..]);

                //FF if the input character was 'n' or 'N'
                var n_mask = Vector128.Equals(vec | bit5, splat_n);

                //c is ['\t', '\n', ' '] (00-ff boolean)
                var space_mask = Vector128.Equals(vec, splat_space) | Vector128.Equals(vec, splat_tab) | Vector128.Equals(vec, splat_newline);
                //shift space left and AND 

                
                // only nya-ify if its space followed by 'n'
                var space_and_n_mask = space_mask.ShiftLeftLogical128BitLane() & n_mask;
                var nya_mask = Vector128.ExtractMostSignificantBits(space_and_n_mask);

                vec.CopyTo(out_bytes.Slice(out_bytes_ptr, 16));
                // try to nya-ify as many as possible in the current vector
                while (nya_mask != 0)
                {
                    var nya_idx = (byte)BitOperations.TrailingZeroCount(nya_mask);
                    out_bytes[out_bytes_ptr + nya_idx + 1] = (byte)'y';
                    // shuffle to shift by amount only known at runtime
                    var shifted = Vector128.Shuffle(vec, indexes + Vector128.Create((byte)(nya_idx + 1)));

                    shifted.CopyTo(out_bytes.Slice(out_bytes_ptr + nya_idx + 2, 16));
                    out_bytes_ptr++;
                    len += 1;
                    nya_mask &= nya_mask - 1;
                }

                out_bytes_ptr += 16;
            }

            return len;
        }
        static int ReplaceAndStutterSse(XorShift32 rng, ReadOnlySpan<byte> in_bytes, int len, Span<byte> out_bytes)
        {
            var out_bytes_ptr = 0;

            var bit5 = Vector128.Create((byte)0b0010_0000);
            var splat_backtick = Vector128.Create((byte)'`');
            var splat_open_brace = Vector128.Create((byte)'{');
            var splat_l = Vector128.Create((byte)'l');
            var splat_r = Vector128.Create((byte)'r');
            var splat_w = Vector128.Create((byte)'w');
            var splat_space = Vector128.Create((byte)' ');
            var splat_tab = Vector128.Create((byte)'\t');
            var splat_newline = Vector128.Create((byte)'\n');
            var indexes = Vector128.Create((byte)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);

            var iter_len = RoundUp16(len);

            for (int i = 0; i < iter_len; i += 16)
            {
                // replace 'l' and 'r' with 'w'
                var vec = Vector128.Create(in_bytes.Slice(i, 16));

                var vec_but_lower = vec | bit5;
                var alpha_mask = Vector128.GreaterThan(vec_but_lower, splat_backtick) & Vector128.GreaterThan(splat_open_brace, vec_but_lower);

                var replace_mask = Vector128.Equals(vec_but_lower, splat_l) | Vector128.Equals(vec_but_lower, splat_r);
                var replaced = Vector128.ConditionalSelect(replace_mask, splat_w, vec_but_lower);

                // make sure only alphabetical characters are lowercased and replaced
                var res = Vector128.ConditionalSelect(alpha_mask, replaced, vec);
                // sometimes, add a stutter if there is a space, tab, or newline followed by any varter
                var space_mask = Vector128.Equals(vec, splat_space) | Vector128.Equals(vec, splat_tab) | Vector128.Equals(vec, splat_newline);
                var space_and_alpha_mask = space_mask.ShiftLeftLogical128BitLane() & alpha_mask;
                var stutter_mask = Vector128.ExtractMostSignificantBits(space_and_alpha_mask);
                res.CopyTo(out_bytes.Slice(out_bytes_ptr, 16));
                if (stutter_mask != 0)
                {
                    //Writes out base string: "... hewwo"
                    //gets stutter idx (first letter after space)
                    //writes out with a dash: "... h-wwo"
                    //    writes out the base string again: "... hewwo"
                    //OR: writes out after the dash:        "... h-hewwo"
                    
                    var stutter_idx = BitOperations.TrailingZeroCount(stutter_mask);

                    // shuffle to shift by amount only known at runtime
                    res = Vector128.Shuffle(res, indexes + Vector128.Create((byte)stutter_idx));
                    
                    res.WithElement(1, (byte)'-').CopyTo(out_bytes.Slice(out_bytes_ptr + stutter_idx, 16));

                    // decide whether to stutter in a branchless way
                    // a branch would mispredict often since this is random
                    var increment = rng.GenBool() ? 2 : 0;
                    res.CopyTo(out_bytes.Slice(out_bytes_ptr + stutter_idx + increment, 16));
                    out_bytes_ptr += increment;
                    len += increment;
                }
                out_bytes_ptr += 16;
            }

            return len;
        }
    }
}