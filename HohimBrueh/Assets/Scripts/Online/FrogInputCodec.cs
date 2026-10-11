using FrogSmashers.Netcode;
using Hindsight.Core;

namespace FrogSmashers.Online
{
    /// <summary>
    /// One Player's input as 4 bytes for the network: <see cref="FrogInput.ToBits"/>, low byte
    /// first. Both machines read the bytes the same way whatever their own byte order.
    /// </summary>
    public sealed class FrogInputCodec : IInputCodec<FrogInput>
    {
        public int Size => 4;

        public void Write(FrogInput input, byte[] buffer, int offset)
        {
            uint bits = input.ToBits();
            buffer[offset] = (byte)bits;
            buffer[offset + 1] = (byte)(bits >> 8);
            buffer[offset + 2] = (byte)(bits >> 16);
            buffer[offset + 3] = (byte)(bits >> 24);
        }

        public FrogInput Read(byte[] buffer, int offset)
        {
            uint bits = buffer[offset]
                        | ((uint)buffer[offset + 1] << 8)
                        | ((uint)buffer[offset + 2] << 16)
                        | ((uint)buffer[offset + 3] << 24);
            return FrogInput.FromBits(bits);
        }
    }
}
