using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace UwUSharp
{
    class XorShift32
    {
        uint State;
        uint Counter;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public XorShift32(ReadOnlySpan<byte> seed)
        {
            State = BitConverter.ToUInt32(seed) | 1;
            Counter = State;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GenUInt32()
        {
            State ^= State << 13;
            State ^= State >> 17;
            State ^= State << 5;
            Counter = unchecked(Counter + 1234567891);
            return unchecked(State + Counter);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GenBits(int bits)
        {
            return GenUInt32() & (uint)((1 << bits) - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GenBool()
        {
            // kinda wasteful but ok
            return GenBits(1) > 0;
        }
    }
}
