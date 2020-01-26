using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace gsudo.Tests
{
    class TestProcess
    {
        public Process Process { get; private set; }
        public int ExitCode => Process.ExitCode;
        string StdOut = null;
        string StdErr = null;

        string OutFile = $"out{Guid.NewGuid()}";
        string ErrFile = $"err{Guid.NewGuid()}";
        string InFile = $"in{Guid.NewGuid()}";

        public TestProcess(string exename, string arguments, string input = "")
        {
            File.WriteAllText(InFile, input);

            this.Process = new Process();
            this.Process.StartInfo = new ProcessStartInfo()
            {
                FileName = "cmd",
                Arguments = $"/c {exename} {arguments} >{OutFile} 2>{ErrFile} <{InFile}",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Maximized,
                CreateNoWindow = false
            };
            this.Process.Start();

            Debug.WriteLine($"Process invoked: {Process.StartInfo.FileName} {Process.StartInfo.Arguments}");
        }

        internal void WriteInput(string input)
        {
            Process.StandardInput.Write(input);
        }

        public string GetStdOut() => StdOut ??= File.ReadAllText(OutFile);
        public string GetStdErr() => StdErr ??= File.ReadAllText(ErrFile);
        public void WaitForExit(int waitMilliseconds = 30000)
        {
            try
            {
                if (!Process.WaitForExit(waitMilliseconds))
                {
                    Process.Kill();
                    Thread.Sleep(500);
                    Assert.Fail("Process still active!");
                }
            }
            finally
            {
                try
                {
                    Debug.WriteLine($"Process Std Output:\n{GetStdOut()}");
                    Debug.WriteLine($"Process Std Error:\n{GetStdErr()}");
                }
                catch { }
            }
        }
    }
}
