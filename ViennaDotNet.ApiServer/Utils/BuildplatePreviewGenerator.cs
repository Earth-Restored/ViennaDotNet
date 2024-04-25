using System.Buffers.Text;
using System.Diagnostics;
using System;
using ViennaDotNet.DB.Models.Player;
using Serilog;
using System.Text;
using Newtonsoft.Json;

namespace ViennaDotNet.ApiServer.Utils
{
    public sealed class BuildplatePreviewGenerator
    {
        private readonly string command;

        public BuildplatePreviewGenerator(string command)
        {
            this.command = command;
        }

        public string? generatePreview(Buildplates.Buildplate buildplate, byte[] serverData)
        {
            bool isNight = buildplate.night;

            // originally read as byte array, later converted to string, reading byte[] is harder, so I just read it as string
            /*byte[]*/
            string previewBytes = string.Empty;
            try
            {
                ProcessStartInfo processStartInfo = new ProcessStartInfo(command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true
                };

                // Start the process
                Process process = new Process
                {
                    StartInfo = processStartInfo
                };
                process.Start();

                Log.Debug($"Started preview generator subprocess with PID {process.Id/*.pid()*/}");
                process.StandardInput.BaseStream.Write(serverData);
                process.StandardInput.BaseStream.Flush();
                if (!process.HasExited)
                {
                    Log.Warning("Preview generator subprocess is still running, waiting for it to exit");
                }
                int exitCode;
                for (; ; )
                {
                    try
                    {
                        process.WaitForExit();
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
                //previewBytes = process.StandardOutput.ReadToEnd();
                StringBuilder builder = new StringBuilder();
                while (process.StandardOutput.Peek() > -1)
                    builder.AppendLine(process.StandardOutput.ReadLine()); // maybe only Append?
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
