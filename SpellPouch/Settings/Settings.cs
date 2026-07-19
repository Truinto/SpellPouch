using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Shared;

#pragma warning disable IDE1006 // ignore lower case fields

namespace SpellPouch
{
    public class Settings : BaseSettings<Settings>
    {
        public override string DefaultPath => Path.Combine(Main.ModPath, "settings.json");
        [JsonInclude] public override int Version { get; set; } = 6;

        [JsonProperty]
        public bool verbose = true;

        [JsonProperty] public bool NewFeatureDefaultOn { get; set; } = true;
        /// <summary>Collection of features explicitly turned off.</summary>
        [JsonProperty] public HashSet<string> Blacklist { get; protected set; } = [];
        /// <summary>Collection of features explicitly turned on.</summary>
        [JsonProperty] public HashSet<string> Whitelist { get; protected set; } = [];

        public static Settings State = TryLoad();

        protected override bool OnUpdate()
        {
            return true;
        }
    }
}
