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
using SpellPouch;
using CodexLib;

namespace Shared
{
    public partial class Main
    {
        private static GUIStyle StyleBox;
        private static GUIStyle StyleLine;

        static partial void OnLoad(UnityModManager.ModEntry modEntry)
        {
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnHideGUI = OnHideGUI;
        }

        public static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            if (StyleBox == null)
            {
                StyleBox = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
                StyleLine = new GUIStyle() { fixedHeight = 1, margin = new RectOffset(0, 0, 4, 4), };
                StyleLine.normal.background = new Texture2D(1, 1);
            }

            if (GUILayout.Button("Reload Ability Groups 'DefGroups.json'", GUILayout.ExpandWidth(false)))
                Patch_AbilityGroups.Reload();
            Checkbox(ref DefGroup.Unlocked, "Show all Ability Groups", b => DefGroup.RefreshUI());
        }

        public static void OnHideGUI(UnityModManager.ModEntry modEntry)
        {
        }

        public static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
        }

        static partial void OnBlueprintsLoaded()
        {
            using var scope = new Scope(Main.ModPath, Main.logger);

            PatchSafe(typeof(Patch_AbilityGroups));
            SubscribeSafe(typeof(Patch_AbilityGroups));
        }
    }
}