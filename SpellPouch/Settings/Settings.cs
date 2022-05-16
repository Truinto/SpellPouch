using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Shared;

namespace SpellPouch
{
    public class Settings : BaseSettings<Settings>
    {
        public Settings() => version = 1;

        [JsonProperty]
        public bool verbose = true;

        public static Settings State = TryLoad(Main.ModPath, "settings.json");
        protected override bool OnUpdate()
        {
            return true;
        }
    }
}
