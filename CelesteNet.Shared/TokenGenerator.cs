using System;
using System.Linq;
using System.Threading;

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

        private object lfsrLock = new object();
        private Random lfsrStepRNG;
        private uint lfsrState, lfsrMask;

        private uint[] shuffleMasks;
        private uint xorKey;

        public TokenGenerator() : this(new Random()) {}
        public TokenGenerator(Random rng) {
            uint RandomUInt() {
                return ((uint) rng.Next(1 << 30)) << 2 | ((uint) rng.Next(1 << 2));
            }

            lfsrStepRNG = rng;

            // Initialize the LFSR to a random non-zero state
            while (lfsrState == 0)
                lfsrState = RandomUInt();

            // Generate a random LFSR mask with a period of 2^32-1 (the maximum)
            // We just use x^(rand) + 1, which is guaranteed to be of maximum length
            lfsrMask = 1u | (1u << rng.Next(0, 31));

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

        public int GenerateToken() {
            uint val;

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

            return unchecked((int) val);
        }

    }
}
