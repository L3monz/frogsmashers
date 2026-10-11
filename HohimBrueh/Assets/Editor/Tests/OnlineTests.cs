using FrogSmashers.Netcode;
using FrogSmashers.Online;
using NUnit.Framework;

namespace FrogSmashers.Tests
{
    /// <summary>The pieces of online play that don't need a network.</summary>
    [TestFixture]
    public class OnlineTests
    {
        [Test]
        public void TheCodecRoundTripsEveryField()
        {
            var codec = new FrogInputCodec();
            var input = FrogInput.Make(right: true, up: true, b: true, x: true, start: true);
            input.XAxis = 127;
            input.YAxis = -64;
            var buffer = new byte[10];

            codec.Write(input, buffer, 3);

            Assert.That(codec.Size, Is.EqualTo(4));
            Assert.That(codec.Read(buffer, 3), Is.EqualTo(input));
            Assert.That(buffer[0] | buffer[1] | buffer[2] | buffer[7], Is.Zero, "writes only its own 4 bytes");
        }

        [Test]
        public void TheCodecUsesAFixedByteOrder()
        {
            var codec = new FrogInputCodec();
            var buffer = new byte[4];

            codec.Write(FrogInput.FromBits(0x04030201u), buffer, 0);

            Assert.That(buffer, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        }
    }
}
