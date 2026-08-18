using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AutoTranslation.Utilities;
using UnityEngine;
using Verse;

namespace AutoTranslation.Translators
{

    public class Translator_ClaudeCode : Translator_BaseOnlineAIModel
    {
        public override string Name => "Claude Code (CLI)";

        public override bool RequiresKey => false;

        // Not used - this translator has no HTTP endpoint
        public override string BaseURL => "";

        private static readonly List<string> KnownModels = new List<string> { "haiku", "sonnet", "opus", "fable" };

        // Each call spawns a ~100-300MB node process; running many at once can exhaust
        // the user's RAM. The manager clamps its queue concurrency to this value.
        public override int MaxConcurrentRequests => 10;

        // Hard backstop: even calls outside the manager queue (test button, batch retry)
        // can never run more than this many CLI processes at the same time
        private static readonly System.Threading.SemaphoreSlim _processGate = new System.Threading.SemaphoreSlim(10);

        public override void Prepare()
        {
            if (Settings == null) Settings = new TranslatorSettings_AIModel();
            Ready = true;
        }

        public override List<string> GetModels()
        {
            // The CLI has no model-list API; offer the well-known aliases
            // (manual entry still accepts any model id)
            return new List<string>(KnownModels);
        }

        protected override string GetResponseUnsafe(string text, string prompt)
        {
            var exe = string.IsNullOrEmpty(Config?.CliPath) ? "claude" : Config.CliPath.Trim();
            var model = (Model ?? "sonnet").Replace("\"", "");
            var args = $"-p --output-format json --model \"{model}\"";
            var stdin = prompt + "\n\n" + text;

            var (stdout, stderr, exitCode) = RunProcess(exe, args, stdin);

            if (exitCode != 0)
            {
                var reason = !string.IsNullOrEmpty(stderr) ? stderr.Trim() : stdout?.Trim() ?? "no output";
                throw new Exception($"Claude Code CLI exited with code {exitCode}: {Truncate(reason, 300)}");
            }

            if (string.IsNullOrEmpty(stdout))
            {
                throw new Exception("Claude Code CLI produced no output");
            }

            return stdout;
        }

        protected override string ParseResponse(string response)
        {
            // --output-format json => {"type":"result","is_error":false,"result":"...","usage":{...}}
            var result = response.GetStringValueFromJson("result");

            if (Regex.IsMatch(response, "\"is_error\"\\s*:\\s*true"))
            {
                throw new Exception($"Claude Code returned an error: {Truncate(result ?? response, 300)}");
            }

            if (result == null)
            {
                throw new Exception($"Failed to parse Claude Code output: {Truncate(response, 300)}");
            }

            return result.Trim();
        }

        private (string stdout, string stderr, int exitCode) RunProcess(string exe, string args, string stdin)
        {
            _processGate.Wait();
            try
            {
                return RunProcessInner(exe, args, stdin);
            }
            finally
            {
                _processGate.Release();
            }
        }

        private (string stdout, string stderr, int exitCode) RunProcessInner(string exe, string args, string stdin)
        {
            Process proc;
            try
            {
                proc = Spawn(exe, args);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // "claude" from npm is a .cmd shim which Process.Start cannot launch directly -
                // retry through the command interpreter (only when no explicit path was given)
                if (exe.IndexOf('\\') >= 0 || exe.IndexOf('/') >= 0) throw;
                proc = Spawn("cmd.exe", $"/c {exe} {args}");
            }

            try
            {
                // Start readers before writing stdin: if the CLI floods stderr first
                // (node warnings etc.), the stdin write would block on a full pipe buffer
                var outTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
                var errTask = Task.Run(() => proc.StandardError.ReadToEnd());

                // net472 cannot set StandardInputEncoding - write UTF-8 bytes directly
                var bytes = Encoding.UTF8.GetBytes(stdin);
                proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                proc.StandardInput.Close();

                if (!proc.WaitForExit(TimeoutMs))
                {
                    try { proc.Kill(); } catch (Exception) { }
                    throw new Exception($"Claude Code CLI timed out after {TimeoutMs / 1000}s");
                }

                // A child process may inherit the pipe's write end and keep it open after the
                // CLI exits - bound the wait and never touch .Result of an unfinished task
                if (!Task.WaitAll(new Task[] { outTask, errTask }, 5000))
                {
                    throw new Exception("Claude Code CLI exited but its output could not be read in time");
                }

                return (outTask.Result, errTask.Result, proc.ExitCode);
            }
            finally
            {
                try { if (!proc.HasExited) proc.Kill(); } catch (Exception) { }
                proc.Dispose();
            }
        }

        private static Process Spawn(string exe, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            return Process.Start(psi);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }

        protected override void DrawConnectionSettings(Listing_Standard ls)
        {
            var prevColor = GUI.color;
            GUI.color = Color.yellow;
            ls.Label("AT_Setting_ClaudeCodeNotice".Translate());
            GUI.color = prevColor;

            ls.Gap(4f);
            ls.Label("AT_Setting_ClaudeCodePath".Translate());
            Config.CliPath = ls.TextEntry(Config.CliPath);
        }

        protected override void DrawBaseUrlSettings(Listing_Standard ls)
        {
            // No HTTP endpoint - nothing to draw
        }
    }
}
