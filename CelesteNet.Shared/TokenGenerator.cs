using System;
using System.IO;
using System.Linq;

namespace Celeste.Mod.CelesteNet {
    /*
    This class generates pseudo-random tokens, which are primarly used as
    connection tokens for the server. The reason for not just using an
    incrementing counter is that those tokens would be predictable, and a
    malicious client could use those predicted token to hijack new UDP
    connections by spamming token datagrams. The tokens by this generator are
    generated via a Galois LFSR with a random polynomial, which is stepped a
    random number of times to prevent correlation attacks, whose output bits
    are randomly shuffled, and then XORed with a random key.
    -Popax21
    */
    public class TokenGenerator {

        public const int MaxLFSRSteps = 4;

        private static readonly uint[] LFSRPolynomials;

        static TokenGenerator() {
            // Read the list of polynomials
            using Stream? stream = typeof(TokenGenerator).Assembly.GetManifestResourceStream("polynomials.bin");
            if (stream == null) {
                LFSRPolynomials = new uint[0];
                return;
            }
            using BinaryReader reader = new BinaryReader(stream);

            LFSRPolynomials = new uint[stream.Length / 4];
            for (int i = 0; i < LFSRPolynomials.Length; i++)
                LFSRPolynomials[i] = reader.ReadUInt32();
        }

        private readonly object lfsrLock = new();
        private readonly Random lfsrStepRNG;
        private uint lfsrState;
        private readonly uint lfsrMask;

        private readonly uint[] shuffleMasks;
        private readonly uint xorKey;

        public TokenGenerator() : this(new()) {}
        public TokenGenerator(Random rng) {
            uint RandomUInt() {
                return ((uint) rng.Next(1 << 30)) << 2 | ((uint) rng.Next(1 << 2));
            }

            if (LFSRPolynomials.Length == 0)
                throw new FileLoadException("Could not load LFSR polynomials", "polynomials.bin");

            lfsrStepRNG = rng;

            // Initialize the LFSR to a random non-zero state
            while (lfsrState == 0)
                lfsrState = RandomUInt();

            // Choose a random LFSR polynomimal
            // We have to flip the order of the bits (=taking the counterpart) as we're using a Galois LFSR instead of a Fibonacci one
            lfsrMask = LFSRPolynomials[rng.Next(LFSRPolynomials.Length)];
            lfsrMask = (lfsrMask >>  1) & 0x55555555 | (lfsrMask <<  1) & 0xaaaaaaaa;
            lfsrMask = (lfsrMask >>  2) & 0x33333333 | (lfsrMask <<  2) & 0xcccccccc;
            lfsrMask = (lfsrMask >>  4) & 0x0f0f0f0f | (lfsrMask <<  4) & 0xf0f0f0f0;
            lfsrMask = (lfsrMask >>  8) & 0x00ff00ff | (lfsrMask <<  8) & 0xff00ff00;
            lfsrMask = (lfsrMask >> 16) & 0x0000ffff | (lfsrMask << 16) & 0xffff0000;

            Logger.Log(LogLevel.DBG, "tokenGen", $"Created TokenGenerator with mask 0x{lfsrMask:x8}");

            // Determine the new locations for each bit by shuffeling a bit index array
            int[] newBitLocs = Enumerable.Range(0, 32).ToArray();
            for (int i = 31; i > 0; i--) {
                int n = rng.Next(0, i+1);

                int t = newBitLocs[n];
                newBitLocs[n] = newBitLocs[i];
                newBitLocs[i] = t;
            }

            // Generate shuffle masks
            shuffleMasks = new uint[32];
            for (int b = 0; b < 31; b++) {
                int rotateAmount = (32 + newBitLocs[b] - b) % 32;
                shuffleMasks[rotateAmount] |= 1u << b;
            }

            // Generate a random XOR key
            xorKey = RandomUInt();
        }

        public uint GenerateToken() {
            uint val = 0;

            // Step the (Galois) LFSR a random number of times
            lock (lfsrLock)
            for (int i = lfsrStepRNG.Next(MaxLFSRSteps); i >= 0; i--) {
                val = lfsrState;
                lfsrState <<= 1;
                if ((val & (1u << 31)) != 0) lfsrState ^= lfsrMask;
            }

            // Shuffle the bits around
            // Each shuffleMasks[s] has a 1 for all bits which should be rotated left by s
            uint sVal = 0;
            for (int s = 0; s < 32; s++)
                sVal |= ((val & shuffleMasks[s]) << s) | ((val & shuffleMasks[s]) >> (32 - s));
            val = sVal;

            // XOR the value with the random XOR key
            val ^= xorKey;

            Logger.Log(LogLevel.DBG, "tokenGen", $"Generated new token {val}");

            return val;
        }

    }
}
