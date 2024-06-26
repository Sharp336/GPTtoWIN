﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace wtp
{
    public class ForkData
    {
        [JsonProperty(PropertyName = "cwd")]
        public string CurrentWorkingDirectory { get; set; }
        [JsonProperty(PropertyName = "env")]
        public IDictionary<string, string> Environment { get; set; }
    }
}
