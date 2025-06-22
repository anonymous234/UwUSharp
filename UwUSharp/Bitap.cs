using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;
using Vec = System.Runtime.Intrinsics.Vector128<byte>;

namespace UwUSharp
{
    //This class only exists to populate pre-computed replacement patterns
    //Unfortunately C# has no constant expressions, so this will always be instantiated and converted at startup.
    //Theoretically you could embed the computed values in the binary somehow but that would not be worth the effort.
    //Strings are also UTF-16, so that's extra re-encoding time. There is a utf-8 literal format but it's a ReadOnlySpan so it can only live in the stack.

    //Therefore it's not performance-critical
    static class BitapConstants
    {
        public readonly static string[] StrPatterns = ["small", "cute", "fluff", "love", "stupid", "what", "meow", "meow"];
        public readonly static string[] StrReplace = ["smol", "kawaii~", "floof", "luv", "baka", "nani", "nya~", "nya~"];

        public readonly static Vec[] Replace = StrReplace.Select(Lib.StrToBytes).ToArray();
        public readonly static Vec[] Masks = GetMasks(StrPatterns);
        public readonly static Vec StartMask = GetStartMask(StrPatterns);
        public readonly static int[] REPLACE_LEN = Lib.GetLen(Replace);
        

        // important note: replacement cannot be more than 2 times longer than the corresponding pattern!
        // this is to prevent increasing the size of the output too much in certain cases
        // another note: this table has a fixed size of 8 and expanding it will require changing the
        // algorithm a little


        /// <summary>
        /// Preprecessing step to associate each character with a mask of locations in each of the 8 pattern strings.
        /// Initializes 256 chunks of 16 bytes to the right values
        /// </summary>
        static Vec[] GetMasks(string[] patterns) {
            static bool IsAsciiAlphabetic(byte b) => (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z');
            //Vector128 is immutable so we use a multidimensional array and copy it

            var buffer = new byte[256,16];
            byte bit5 = 0b0010_0000;
            int i_word = 0;
            foreach (var pattern in patterns.Select(Encoding.UTF8.GetBytes)) {
                // offset masks so the last character maps to the last bit of each 16-bit lane
                // this is useful for movemask later
                var offset = 16 - pattern.Length;
                for(int i_letter=0; i_letter < pattern.Length; i_letter++) {
                    
                    var idx = i_word * 16 + i_letter + offset;
                    buffer[pattern[i_letter], idx / 8] |= (byte)(1 << (idx % 8));

                    // make sure to be case insensitive
                    if (IsAsciiAlphabetic(pattern[i_letter]))
                    {
                        buffer[pattern[i_letter] ^ bit5, idx / 8] |= (byte)(1 << (idx % 8));
                    }
                }
                i_word += 1;
            }
            
            //No LINQ for multidimensional arrays :(
            var res = new Vec[256];
            for(int i=0; i < 256; i++) {
                res[i] = Vector128.LoadUnsafe(ref buffer[i,0]);
            }
            return res;
        }

        /// <summary>
        /// Get a mask that indicates the first character for each pattern
        /// </summary>
        static Vec GetStartMask(string[] patterns) {
            var i = 0;
            var buffer = new byte[16];
            foreach (var string_utf8 in patterns.Select(Encoding.UTF8.GetBytes)) {
                int j = 16 - string_utf8.Length;
                var idx = i * 16 + j;
                buffer[idx / 8] |= (byte)(1 << (idx % 8));
                i += 1;
            }
            return Vector128.Create(buffer);
        }
    }
    internal class Bitap {
        public struct Match
        {
            public int MatchLen;
            public Vec Replace;
            public int ReplaceLen;
        }
        public struct Bitap8x16
        {
            public Vec vec;
            public Bitap8x16() { 
                vec = Vec.Zero;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Match? Next(byte c) {
                vec = Vector128.BitwiseOr(Vector128.ShiftLeft(vec.AsInt16(), 1).AsByte(), BitapConstants.StartMask);
                var mask = BitapConstants.Masks[c];
                vec = Vector128.BitwiseAnd(vec, mask);

                var match_mask = Vector128.ExtractMostSignificantBits(vec) & 0xAAAA;
                if(match_mask != 0) {
                    var match_idx = BitOperations.TrailingZeroCount(match_mask) / 2;
                    return new Match {
                        MatchLen = BitapConstants.StrPatterns[match_idx].Length,
                        Replace = BitapConstants.Replace[match_idx],
                        ReplaceLen = BitapConstants.REPLACE_LEN[match_idx],
                    };
                }
                return null;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset()
            {
                vec = Vec.Zero;
            }
        }        
    }

    //This class was used to have explicit aligned access, but it doesn't seem to measurably improve performance
    /*
    unsafe class AlignedVecArray : IDisposable
    {
        readonly byte* array;
        public readonly int Length;
        public AlignedVecArray(ICollection<byte[]> arrays)
        {
            array = (byte*)NativeMemory.AlignedAlloc((nuint)arrays.Count * 16, 16);
            Length = arrays.Count;
            for (int i = 0; i < arrays.Count; i++)
            {
                var arr = arrays.ElementAt(i);
                Marshal.Copy(arr, 0, (IntPtr)(array + i * 16), arr.Length);
            }
        }
        public Vec this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Vector128.LoadAligned(array + i * 16);
        }
        public void Dispose()
        {
            NativeMemory.AlignedFree(array);
        }
    }
    */
}