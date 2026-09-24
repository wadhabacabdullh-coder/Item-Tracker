using NUnit.Framework;
using ShadowContract.Core;

namespace ShadowContract.Tests
{
    public class SmokeTests
    {
        [Test]
        public void VectorMath()
        {
            var v = new Vec2(3, 4);
            Assert.AreEqual(5f, v.Length, 1e-5f);
        }
    }
}
