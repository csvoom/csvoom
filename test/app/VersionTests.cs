using System.Reflection;
using Xunit;

namespace CSVoom.test.app
{
    public class VersionTests
    {
        [Fact]
        public void TestAssemblyVersion()
        {
            // Note: In some environments, the test might be running against a different assembly 
            // than the one we just built, or the metadata might not be updated until a full rebuild.
            // But we want to verify what's expected.
            var assembly = typeof(CSVoom.app.Parser).Assembly;
            var version = assembly.GetName().Version?.ToString();
            
            Assert.Equal("1.2.0.0", version);
        }
    }
}
