using BaseX;
using FrooxEngine;
using FrooxEngine.UIX;
using HarmonyLib;
using NeosModLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ComponentAttacherRecents
{
    public class ComponentAttacherRecents : NeosMod
    {
        internal static ModConfiguration Config;

        private const string RecentsPath = "/Recent";

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<int> RecentCap = new("RecentCap", "How many recent components are tracked. 0 to disable.", () => 32, valueValidator: value => value >= 0);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<List<string>> RecentComponents = new("RecentComponents", "Recent Components", () => new List<string> { "FrooxEngine.ValueMultiDriver`1" }, true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> TrackConcreteComponents = new("TrackConcreteComponents", "Whether the concrete version of a recent generic component gets added to the category.", () => true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> TrackGenericComponents = new("TrackGenericComponents", "Whether the generic version of a recent component gets added to the category.", () => true);

        private static CategoryNode<Type> recentsCategory;
        public override string Author => "Banane9";
        public override string Link => "https://github.com/Banane9/NeosComponentAttacherRecents";
        public override string Name => "ComponentAttacherRecents";
        public override string Version => "1.1.0";

        private static List<string> Recents => Config.GetValue(RecentComponents);

        private static CategoryNode<Type> RecentsCategory
        {
            get => recentsCategory;
            set
            {
                recentsCategory = value;

                foreach (var typeName in Config.GetValue(RecentComponents).Take(Config.GetValue(RecentCap)))
                    recentsCategory.AddElement(WorkerManager.GetType(typeName));
            }
        }

        public override void OnEngineInit()
        {
            var harmony = new Harmony($"{Author}.{Name}");
            Config = GetConfiguration();
            Config.Set(RecentComponents, Config.GetValue(RecentComponents));
            Config.Save(true);
            harmony.PatchAll();

            Config.OnThisConfigurationChanged += Config_OnThisConfigurationChanged;

            Engine.Current.OnReady += () =>
            {
                RecentsCategory = WorkerInitializer.ComponentLibrary.GetSubcategory(RecentsPath);

                if (ModLoader.Mods().FirstOrDefault(mod => mod.Name == "ComponentAttacherSearch") is NeosModBase searchMod
                 && (searchMod.GetConfiguration()?.TryGetValue(new ModConfigurationKey<HashSet<string>>("ExcludedCategories"), out var excludedCategories) ?? false))
                    excludedCategories.Add(RecentsPath);
            };
        }

        private static void TrimRecentComponents()
        {
            var remove = Recents.Count - Config.GetValue(RecentCap);
            if (remove <= 0)
                return;

            var recentsToRemove = Recents.Take(remove).ToArray();
            RecentsCategory._elements.RemoveAll(type => recentsToRemove.Contains(type.FullName));
            RecentsCategory._sorted = false;

            Recents.RemoveRange(0, remove);
        }

        private void Config_OnThisConfigurationChanged(ConfigurationChangedEvent configurationChangedEvent)
        {
            if (configurationChangedEvent.Key == RecentCap)
                TrimRecentComponents();
        }

        [HarmonyPatch(typeof(ComponentAttacher))]
        private static class ComponentAttacherPatches
        {
            [HarmonyPostfix]
            [HarmonyPatch("BuildUI")]
            public static void BuildUIPostfix(string path, bool genericType, SyncRef<Slot> ____uiRoot)
            {
                if (genericType)
                    return;

                if ((string.IsNullOrEmpty(path) || path == "/")
                 && ____uiRoot.Target.GetComponentInChildren<ButtonRelay<string>>(relay => relay.Argument == RecentsPath) is ButtonRelay<string> relay)
                {
                    relay.Slot.OrderOffset = long.MinValue + 1;
                    return;
                }
            }

            private static void AddRecentComponent(Type type)
            {
                if (!Recents.Remove(type.FullName))
                    RecentsCategory.AddElement(type);

                Recents.Add(type.FullName);
            }

            [HarmonyPostfix]
            [HarmonyPatch("OnAddComponentPressed")]
            private static void OnAddComponentPressedPostfix(string typename)
            {
                updateRecentComponents(WorkerManager.GetType(typename));
            }

            [HarmonyPrefix]
            [HarmonyPatch("OnCreateCustomType")]
            private static void OnCreateCustomTypePrefix(ComponentAttacher __instance)
            {
                updateRecentComponents(__instance.GetCustomGenericType());
            }

            private static void updateRecentComponents(Type type)
            {
                if (type == null || type.ContainsGenericParameters || !WorkerManager.IsValidGenericType(type, true))
                    return;

                if (!type.IsGenericType || Config.GetValue(TrackConcreteComponents))
                    AddRecentComponent(type);

                if (type.IsGenericType && Config.GetValue(TrackGenericComponents))
                    AddRecentComponent(type.GetGenericTypeDefinition());

                TrimRecentComponents();
            }
        }
    }
}