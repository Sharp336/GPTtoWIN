using System;
using System.Collections.Generic;
using System.IO;

namespace tterm
{
    public class Profile
    {
        public string Command { get; set; } = Environment.GetEnvironmentVariable("COMSPEC") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        public string[] Arguments { get; set; } = new[] { "/K", "echo Welcome to the Terminal Emulator && echo ORIGIN_REPOSITORY = %ORIGIN_REPOSITORY%" };
        public string CurrentWorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public IDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string> { { "ORIGIN_REPOSITORY", "https://github.com/Sharp336/GPTtoWIN" } };
        public string[] Regexps { get; set; } = { @"[a-zA-Z]:\\[^>]+>" };
    }
}
