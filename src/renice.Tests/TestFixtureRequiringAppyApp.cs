using System;
using System.IO;

namespace renice.Tests
{
    public abstract class TestFixtureRequiringAppyApp
    {
        protected static string AppyApp;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            var myAsm = new Uri(typeof(Tests).Assembly.Location).LocalPath;
            var myFolder = Path.GetDirectoryName(myAsm);
            var seek = Path.Combine(myFolder, "AppyApp.exe");
            if (!File.Exists(seek))
            {
                Assert.Fail(
                    $"Can't find AppyApp.exe in '{myFolder}'"
                );
            }

            AppyApp = seek;
        }
    }
}