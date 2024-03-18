﻿using System.Collections.Generic;
using System;
using System.IO;
using System.ComponentModel;

namespace tterm
{
    public class Config
    {
        public bool AllowTransparancy { get; set; }
        public int Columns { get; set; } = 82;
        public int Rows { get; set; } = 17;
        public int Port { get; set; } = 5001;
        public Profile Profile { get; set; } = new Profile();
    }
    public class Profile : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public string Command { get; set; } = Environment.GetEnvironmentVariable("COMSPEC") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        public string[] Arguments { get; set; } = new[] { "/K", "echo Welcome to the Terminal Emulator && echo ORIGIN_REPOSITORY = %ORIGIN_REPOSITORY%" };
        public string CurrentWorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public IDictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string> { { "ORIGIN_REPOSITORY", "https://github.com/Sharp336/GPTtoWIN" } };
        public List<PromptRegexp> PromptRegexps { get; set; }
    }
    public class PromptRegexp
    {
        public string Name { get; set; } = "";
        public string Regex { get; set; } = "";
        public string Reply { get; set; } = null;
        public bool IsOn { get; set; } = true;

    }

}
