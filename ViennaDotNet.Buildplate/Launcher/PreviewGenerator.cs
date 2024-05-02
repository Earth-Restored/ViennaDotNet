using System.Buffers.Text;
using System.Diagnostics;
using System;
using ViennaDotNet.DB.Models.Player;
using Serilog;
using System.Text;
using Newtonsoft.Json;

namespace ViennaDotNet.Buildplate.Launcher
{
    public sealed class PreviewGenerator
    {
        private readonly string javaCmd;
	private readonly FileInfo fountainJar;

        public PreviewGenerator(string javaCmd, string fountainJar)
        {
            this.javaCmd = javaCmd;
            this.fountainJar = new FileInfo(fountainJar);
        }

        // TODO: fuck this and port the preview generation from https://github.com/Project-Genoa/Fountain-bridge/blob/master/src/main/java/micheal65536/fountain/preview/PreviewGenerator.java
        public string? generatePreview(byte[] serverData, bool isNight)
        {
            // originally read as byte array, later converted to string, reading byte[] is harder, so I just read it as string
            /*byte[]*/
            string previewBytes;
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo(javaCmd, new string[] { "-cp", fountainJar.FullName, "micheal65536.fountain.preview.PreviewGenerator" })
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                };

                // Start the process
                Process process = new Process
                {
                    StartInfo = processStartInfo
                };
                process.Start();

                Log.Debug($"Started preview generator subprocess with PID {process.Id}");
                try
                {
                    process.StandardInput.AutoFlush = false;
                    process.StandardInput.Write(Encoding.UTF8.GetString(serverData));
                    process.StandardInput.Flush();
                } catch (Exception ex)
                {
                    string? @out;
                    try
                    {
                        @out = process.StandardOutput.ReadToEnd();
                    } catch { }
                    string? error;
                    try
                    {
                        error = process.StandardError.ReadToEnd();
                    }
                    catch { }
                }
                if (!process.HasExited)
                    Log.Warning("Preview generator subprocess is still running, waiting for it to exit");

                int exitCode;
                for (; ; )
                {
                    try
                    {
                        if (!process.WaitForExit(TimeSpan.FromSeconds(10.0)))
                            process.Kill();
                        exitCode = process.ExitCode;
                        break;
                    }
                    catch (ThreadAbortException)
                    {
                        continue;
                    }
                }
                Log.Debug($"Preview generator subprocess finished with exit code {exitCode}");
                // might not work, idk...
                StringBuilder builder = new StringBuilder();
                while (process.StandardOutput.Peek() > -1)
                    builder.AppendLine(process.StandardOutput.ReadLine()); // maybe only Append?

                previewBytes = process.StandardOutput.ReadToEnd();

                previewBytes = builder.ToString();
            }
            catch (IOException exception)
            {
                Log.Error($"Error while running buildplate preview generator subprocess: {exception}");
                return null;
            }

            Dictionary<string, object> previewObject;
            try
            {
                previewObject = JsonConvert.DeserializeObject<Dictionary<string, object>>(previewBytes)!;
            }
            catch (Exception exception)
            {
                Log.Error($"Error while processing buildplate preview generator response: {exception}");
                return null;
            }

            previewObject["isNight"] = isNight;

            string previewJson = JsonConvert.SerializeObject(previewObject);

            string previewBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(previewJson));

            return previewBase64;
        }
    }
}
