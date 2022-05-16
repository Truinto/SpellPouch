using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;
using UnityEngine;
using UnityModManagerNet;
using Newtonsoft.Json;
using HarmonyLib;

namespace Shared
{
    public partial class Main
    {
        static partial void OnLoad(UnityModManager.ModEntry modEntry)
        {
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnHideGUI = OnHideGUI;
        }

        public static void OnGUI(UnityModManager.ModEntry obj)
        {
            throw new NotImplementedException();
        }

        public static void OnHideGUI(UnityModManager.ModEntry obj)
        {
            throw new NotImplementedException();
        }

        public static void OnSaveGUI(UnityModManager.ModEntry obj)
        {
            throw new NotImplementedException();
        }
    }
}