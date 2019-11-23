﻿using gsudo.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace gsudo
{
    // Regular Windows Console app host service. (not ConPTY).
    // Assumes authentication succeded
    class WinPtyHostProcess
    {
        private NamedPipeServerStream pipe;
        private string lastInboundMessage = null;
        private Process process;
        public WinPtyHostProcess(NamedPipeServerStream pipe)
        {
            this.pipe = pipe;
        }

        internal async Task Start(ElevationRequest request)
        {
            try
            {
                process = ProcessStarter.StartInProcessRedirected(request.FileName, request.Arguments, request.StartFolder);

                Globals.Logger.Log($"Process ({process.Id}) started: {request.FileName} {request.Arguments}", LogLevel.Debug);

                var t1 = process.StandardOutput.ConsumeOutput((s) => WriteToPipe(s));
                var t2 = process.StandardError.ConsumeOutput((s) => WriteToErrorPipe(s));
                var t3 = new StreamReader(pipe, Globals.Encoding).ConsumeOutput((s) => ReadFromPipe(s, process));

                int i = 0;
                while (!process.WaitForExit(0) && pipe.IsConnected && !process.HasExited)
                {
                    await Task.Delay(10);
                    try
                    {
                        i = (i + 1) % 50;
                        if (i==0) await pipe.WriteAsync("\0"); // Sending a KeepAlive is mandatory to detect if the pipe has disconnected.
                    } 
                    catch (IOException)
                    {
                        break;
                    }
                }
                // Globals.Logger.Log($"Process {process.Id} wait loop ended.", LogLevel.Debug);

                if (process.HasExited && pipe.IsConnected)
                {
                    // we need to ensure that all process output is read.
                    while(ShouldWait(process.StandardError) || ShouldWait(process.StandardOutput))
                        await Task.Delay(1);

                    await pipe.FlushAsync();
                    pipe.WaitForPipeDrain();
                    await pipe.WriteAsync($"{Globals.TOKEN_EXITCODE}{process.ExitCode}{Globals.TOKEN_EXITCODE}");
                    await pipe.FlushAsync();
                    pipe.WaitForPipeDrain();
                }
                else
                {
                    TerminateHostedProcess();
                }

                if (pipe.IsConnected)
                {
                    pipe.WaitForPipeDrain();
                }
                pipe.Close();
            }
            catch (Exception ex)
            {
                Globals.Logger.Log(ex.ToString(), LogLevel.Error);
                await pipe.WriteAsync(Globals.TOKEN_ERROR + "Server Error: " + ex.ToString() + "\r\n");
                
                await pipe.FlushAsync();
                pipe.WaitForPipeDrain();
                pipe.Close();
                return;
            }
        }

        private bool ShouldWait(StreamReader streamReader)
        {
            try
            {
                return !streamReader.EndOfStream;
            }
            catch
            {
                return false;
            }
        }

        private void TerminateHostedProcess()
        {
            Globals.Logger.Log($"Killing process {process.Id} {process.ProcessName}", LogLevel.Debug);

            if (process.HasExited) return;

            process.SendCtrlC(true);

            if (process.CloseMainWindow())
                process.WaitForExit(100);

            if (!process.HasExited)
            {
                var p = Process.Start(new ProcessStartInfo()
                {
                    FileName = "taskkill",
                    Arguments = $"/PID {process.Id} /T",
                    WindowStyle = ProcessWindowStyle.Hidden

                });
                p.WaitForExit();
            }   
        }

        static readonly string[] TOKENS = new string[] { "\0", Globals.TOKEN_KEY_CTRLBREAK, Globals.TOKEN_KEY_CTRLC};
        private async Task ReadFromPipe(string s, Process process)
        {
            var tokens = new Stack<string>(StringTokenizer.Split(s, TOKENS));

            while (tokens.Count > 0)
            {
                var token = tokens.Pop();

                if (token == "\0") continue;

                if (token == Globals.TOKEN_KEY_CTRLC)
                {
                    ProcessExtensions.SendCtrlC(process);
                    await Task.Delay(10);
                    pipe.WaitForPipeDrain();
                    await WriteToErrorPipe("^C\r\n");
                    lastInboundMessage = null;
                    continue;
                }

                if (token == Globals.TOKEN_KEY_CTRLBREAK)
                {
                    ProcessExtensions.SendCtrlC(process, true);
                    await Task.Delay(10);
                    pipe.WaitForPipeDrain();
                    await WriteToErrorPipe("^BREAK\r\n");
                    lastInboundMessage = null;
                    continue;
                }

                if (lastInboundMessage == null)
                    lastInboundMessage = token;
                else
                    lastInboundMessage += token;

                await process.StandardInput.WriteAsync(token);
            }
        }

        private async Task WriteToErrorPipe(string s)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(s);
            Console.ResetColor();
            await pipe.WriteAsync(Globals.TOKEN_ERROR + s + Globals.TOKEN_ERROR);
            await pipe.FlushAsync();
        }

        private async Task WriteToPipe(string s)
        {
            if (!string.IsNullOrEmpty(lastInboundMessage)) // trick to avoid echoing the input command, as the client has already showed it.
            {
                int c = EqualCharsCount(s, lastInboundMessage);
                if (c > 0)
                {
                    s = s.Substring(c);
                    lastInboundMessage = lastInboundMessage.Substring(c);
                }
                if (Globals.Debug && !string.IsNullOrEmpty(s)) Globals.Logger.Log($"Last input command was: {s}", LogLevel.Debug);
                
            }
            if (string.IsNullOrEmpty(s)) return; // suppress chars n s;

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write(s);
            await pipe.WriteAsync(s);
            await pipe.FlushAsync();
        }

        private int EqualCharsCount(string s1, string s2)
        {
            int i = 0;
            for (; i < s1.Length && i < s2.Length && s1[i] == s2[i]; i++)   
            { }
            return i;
        }
    }
}
