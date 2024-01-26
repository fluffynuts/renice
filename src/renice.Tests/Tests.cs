using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Principal;
using System.Threading.Tasks;
using PeanutButter.Utils;

namespace renice.Tests;

[TestFixture]
public class Tests : TestFixtureRequiringAppyApp
{
    [TestFixture]
    public class WhenUsingSymbolicName : TestFixtureRequiringAppyApp
    {
        [TestCase("realtime", ProcessPriorityClass.RealTime)]
        [TestCase("real-time", ProcessPriorityClass.RealTime)]
        [TestCase("rt", ProcessPriorityClass.RealTime)]
        [TestCase("high", ProcessPriorityClass.High)]
        [TestCase("abovenormal", ProcessPriorityClass.AboveNormal)]
        [TestCase("above-normal", ProcessPriorityClass.AboveNormal)]
        [TestCase("above", ProcessPriorityClass.AboveNormal)]
        [TestCase("normal", ProcessPriorityClass.Normal)]
        [TestCase("belownormal", ProcessPriorityClass.BelowNormal)]
        [TestCase("below-normal", ProcessPriorityClass.BelowNormal)]
        [TestCase("below", ProcessPriorityClass.BelowNormal)]
        [TestCase("idle", ProcessPriorityClass.Idle)]
        public async Task ShouldSetCorrectPriority(
            string arg,
            ProcessPriorityClass expected
        )
        {
            // Arrange
            using var io = StartAppyApp();
            // Act
            await Program.Main(
                [
                    "-n",
                    arg,
                    "-p",
                    $"{io.ProcessId}"
                ]
            );
            // Assert
            Expect(io.Process.HasExited)
                .To.Be.False();
            io.WaitForOutput(StandardIo.StdOut, s => s == "zzz", 5000);
            Expect(io.Process.PriorityClass)
                .To.Equal(expected);
        }

        [TestCase("realtime", ProcessPriorityClass.RealTime)]
        [TestCase("real-time", ProcessPriorityClass.RealTime)]
        [TestCase("rt", ProcessPriorityClass.RealTime)]
        [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
        public async Task ShouldSetRealTimePriorityIfElevated(
            string arg,
            ProcessPriorityClass expected
        )
        {
            var identity = WindowsIdentity.GetCurrent();
            if (!identity.Owner!.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
            {
                Assert.Ignore("This test is likely to fail as it's not being run elevated");
            }

            // Arrange
            using var io = StartAppyApp();
            // Act
            await Program.Main(
                [
                    "-n",
                    arg,
                    "-p",
                    $"{io.ProcessId}"
                ]
            );
            // Assert
            Expect(io.Process.HasExited)
                .To.Be.False();
            io.WaitForOutput(StandardIo.StdOut, s => s == "zzz", 5000);
            Expect(io.Process.PriorityClass)
                .To.Equal(expected);
        }

        [Test]
        public async Task ShouldBeAbleToSmooshArgs()
        {
            // Arrange
            using var io = StartAppyApp();
            // Act
            await Program.Main(
                [
                    "-nhigh",
                    $"-p{io.ProcessId}"
                ]
            );
            // Assert
            io.WaitForOutput(StandardIo.StdOut, s => s == "zzz", 5000);
            Expect(io.Process.PriorityClass)
                .To.Equal(ProcessPriorityClass.High);
        }
    }

    private static IProcessIO StartAppyApp()
    {
        return ProcessIO.Start(
                AppyApp,
                Debugger.IsAttached
                    ? "600000"
                    : "10000"
        );
    }
}