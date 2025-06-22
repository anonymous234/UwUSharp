using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;
using Vec = System.Runtime.Intrinsics.Vector128<byte>;
//Corresponds to lib.rs

namespace UwUSharp
{
    static class VecExtensions
    {
        static readonly Vec ShiftLeftIndexVector = Vector128.Create(0xFF, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E);
        static readonly Vec ShiftRightIndexVector = Vector128.Create(0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0xFF);
        /// <summary>
        /// Shifts vector left numerically, which means moving each byte to the RIGHT, since bit shifts use big-endian convention but the vector is little-endian (it's the Intel convention)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vec ShiftLeftLogical128BitLane(this Vec inp)
        {
            
            if (Sse2.IsSupported)
            {
                return Sse2.ShiftLeftLogical128BitLane(inp, 1);
            }
            else if (AdvSimd.IsSupported)
            {
                return AdvSimd.ExtractVector128(Vec.Zero, inp, 15);
                //Alternatively:
                //var res = AdvSimd.ExtractVector128(inp, inp, 15);
                //return AdvSimd.Insert(res, 0, 0);
                //There's also Sve.InsertIntoShiftedVector. But then we'd need to use Vector<byte> instead of Vector128<byte>
                //That would also involve a register transfer (unless Vector128 was already using SVE? Which would make the current approach slower)
            }
            else if (System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported)
            {
                return System.Runtime.Intrinsics.Wasm.PackedSimd.Swizzle(inp, ShiftLeftIndexVector);
            }
            else
            {
                byte[] b = new byte[17];
                inp.CopyTo(b, 1);
                return Vector128.Create(b);
            }

        }
        /// <summary>
        /// Shifts vector right numerically, which means moving each byte to the LEFT, since bit shifts use big-endian convention but the vector is little-endian (it's the Intel convention)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Vec ShiftRightLogical128BitLane(this Vec inp)
        {
            if (Sse2.IsSupported)
            {
                return Sse2.ShiftRightLogical128BitLane(inp, 1);
            }
            else if (AdvSimd.IsSupported)
            {
                //Best way I could find:
                return AdvSimd.ExtractVector128(inp, Vec.Zero, 1);
                //Alternatively:
                //inp = AdvSimd.ExtractVector128(inp, inp, 1);
                //return AdvSimd.Insert(inp, 15, 0);
            }
            else if (System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported)
            {
                return System.Runtime.Intrinsics.Wasm.PackedSimd.Swizzle(inp, ShiftRightIndexVector);
            }
            else
            {
                //the slow way (should almost never get here)
                byte[] b = new byte[17];
                inp.CopyTo(b);
                return Vector128.Create(b, 1);
            }
        }
        internal static string AsStr(this Vec c)
        {
            byte[] buffer = new byte[16];
            c.CopyTo(buffer);
            return Encoding.UTF8.GetString(buffer);
        }
    }
}