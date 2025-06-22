using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text;

namespace UwUSharp.Tests
{
    [TestClass]
    public sealed class BitapTests
    {
        static void AssertMatch(Bitap.Bitap8x16 b, string s, int replace_len)
        {
            for (int i = 0; i < s.Length-1; i++)
            {
                Assert.IsNull(b.Next((byte)s[i]));
            }
            var Next = b.Next((byte)s[^1]);
            Assert.IsNotNull(Next);
            Assert.AreEqual(s.Length, Next.Value.MatchLen);
            Assert.AreEqual(replace_len, Next.Value.ReplaceLen);
        }
        static void AssertNoMatch(Bitap.Bitap8x16 b, string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                Assert.IsNull(b.Next((byte)s[i]));
            }
        }
        [TestMethod]
        public void BitapTest()
        {
            Assert.IsTrue(Vector128.IsHardwareAccelerated, "sse4.1 feature not detected!");
            
            var b = new Bitap.Bitap8x16();

            AssertMatch(b, "cute", 7);
            b.Reset();
            AssertMatch(b, "what", 4);
            AssertNoMatch(b, "whaa");
            AssertMatch(b, "WhAt", 4);
        }

    }

    [TestClass]
    public sealed class UwuTests
    {
        [TestMethod]
        public void TestNyaifySse()
        {
            var temp_bytes1 = new byte[1024];
            var temp_bytes2 = new byte[1024];

            var s = "a n"u8;
            var res_bytes = Lib.Uwuify(s, temp_bytes1, temp_bytes2);
            var res = Encoding.UTF8.GetString(res_bytes);
            Assert.AreEqual("a ny", res);
        }
        [TestMethod]
        public void TestUwuifySse()
        {
            var temp_bytes1 = new byte[1024];
            var temp_bytes2 = new byte[1024];
            var s = "Hey, I think I really love you. Do you want a headpat?"u8;
            var res_bytes = Lib.Uwuify(s, temp_bytes1, temp_bytes2);
            var res = Encoding.UTF8.GetString(res_bytes);
            Assert.AreEqual(
                    "hey, (ꈍᴗꈍ) i think i weawwy wuv you. ^•ﻌ•^ do y-you want a headpat?",
                    res
                );
        }
        [TestMethod]
        public void TestUwuifyStringSse()
        {
            var s = "Hey, I think I really love you. Do you want a headpat?";
            var res = Lib.Uwuify(s);
            Assert.AreEqual(
                "hey, (ꈍᴗꈍ) i think i weawwy wuv you. ^•ﻌ•^ do y-you want a headpat?",
                res
            );
        }
        [TestMethod]
        public void TestLongStringSse()
        {
            var longText ="""
                Call me Ishmael. Some years ago—never mind how long precisely—having
                little or no money in my purse, and nothing particular to interest me
                on shore, I thought I would sail about a little and see the watery part
                of the world. It is a way I have of driving off the spleen and
                regulating the circulation. Whenever I find myself growing grim about
                the mouth; whenever it is a damp, drizzly November in my soul; whenever
                I find myself involuntarily pausing before coffin warehouses, and
                bringing up the rear of every funeral I meet; and especially whenever
                my hypos get such an upper hand of me, that it requires a strong moral
                principle to prevent me from deliberately stepping into the street, and
                methodically knocking people's hats off—then, I account it high time to
                get to sea as soon as I can. This is my substitute for pistol and ball.
                With a philosophical flourish Cato throws himself upon his sword; I
                quietly take to the ship. There is nothing surprising in this. If they
                but knew it, almost all men in their degree, some time or other,
                cherish very nearly the same feelings towards the ocean with me.
                """.ReplaceLineEndings("\n");
            string expected = """
                caww me ishmaew. some yeaws ago—nevew m-mind how w-wong pwecisewy—having
                w-wittwe ow n-nyo money in my p-puwse, 😳😳😳 and nyothing p-pawticuwaw t-to intewest me
                o-on showe, 😳😳😳 i thought i wouwd saiw about a wittwe and see the watewy pawt
                of the wowwd. o.O i-it is a way i have of dwiving off the spween a-and
                weguwating the ciwcuwation. ( ͡o ω ͡o ) w-whenevew i find mysewf gwowing gwim about
                the mouth; whenevew i-it is a damp, (U ﹏ U) dwizzwy nyovembew i-in my souw; whenevew
                i-i find mysewf invowuntawiwy pausing befowe coffin wawehouses, and
                bwinging u-up the weaw of evewy funewaw i meet; and especiawwy whenevew
                my hypos get such a-an uppew hand of me, (///ˬ///✿) that it wequiwes a-a stwong mowaw
                p-pwincipwe to p-pwevent me fwom d-dewibewatewy stepping into the stweet, >w< and
                methodicawwy k-knocking peopwe's hats off—then, rawr i account i-it high time to
                get to sea as soon as i can. this is my substitute fow pistow and baww. mya
                with a-a phiwosophicaw fwouwish cato t-thwows himsewf u-upon his swowd; i-i
                quietwy take to the ship. ^^ thewe is nyothing suwpwising in this. 😳😳😳 i-if they
                but knew i-it, awmost aww men in theiw d-degwee, mya some time o-ow othew, 😳
                chewish vewy nyeawwy t-the same feewings towawds the ocean w-with me.
                """.ReplaceLineEndings("\n");

            var res = Lib.Uwuify(longText);
            Assert.AreEqual(
                expected,
                res
            );
        }
        [TestMethod]
        public void EntireTextOfWarAndPeaceTest()
        {
            //https://www.gutenberg.org/cache/epub/2600/pg2600.txt
            //Note: file has UTF-8 BOM which affects the result
            byte[] input = File.ReadAllBytes("pg2600.txt");
            string hash = Convert.ToHexStringLower(SHA256.HashData(input));
            Assert.AreEqual(3_359_652, input.Length, "File pg2600.txt has been modified");
            Assert.AreEqual("d4d61133bac3ddd87280783dcbd52aee85043961ea186ee422de58fffd22a00f", hash, "File pg2600.txt has been modified");

            MemoryStream result = new();
            //Warning: buffer size also affects result
            //This kind of big deterministic test is a bad idea in general, but it's the best way to know it works in this case
            foreach (byte[] buffer in input.Chunk(1 << 16))
            {
                var res = Lib.Uwuify(buffer);
                result.Write(res);
            }
            File.WriteAllBytes("pg2600_out.txt", result.ToArray());

            hash = Convert.ToHexStringLower( SHA256.HashData(result.ToArray()) );
            Assert.AreEqual("465704876c01084f067b50431928be0a77a7da65c26acbcb513be6b61aec451d", hash);
        }
        [TestMethod]
        public void ParallelEntireTextOfWarAndPeaceTest()
        {
            //https://www.gutenberg.org/cache/epub/2600/pg2600.txt
            //Note: file has UTF-8 BOM which affects the result
            using var input = File.OpenRead("pg2600.txt");
            using var output = new MemoryStream();

            Lib.StreamUwu(input, output, 16);

            var result = output.ToArray();
            Assert.AreEqual(3_924_374, result.Length);
            string hash = Convert.ToHexStringLower(SHA256.HashData(result));
            Assert.AreEqual("465704876c01084f067b50431928be0a77a7da65c26acbcb513be6b61aec451d", hash);
        }
    }

}
