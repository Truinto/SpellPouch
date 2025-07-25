﻿// #define VERBOSE

using HarmonyLib;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.UnitSettings;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.UI.MVVM._VM.ActionBar;
using Kingmaker.UI.ActionBar;
using Kingmaker.UI.MVVM._VM.Tooltip.Templates;
using Newtonsoft.Json;
using UnityEngine;
using UniRx;
using System.IO;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.Blueprints.Facts;
using Kingmaker.EntitySystem;
using Kingmaker.UI.DragNDrop;
using Kingmaker.UI.MVVM._PCView.ActionBar;
using UnityEngine.EventSystems;
using Kingmaker;
using Owlcat.Runtime.UI.Tooltips;
using Kingmaker.UI.MVVM;
using JetBrains.Annotations;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.MVVM._PCView.InGame;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.UnitLogic.Commands;
using Owlcat.Runtime.UI.Controls.Button;
using Shared;
using CodexLib;

namespace SpellPouch
{
    [PatchInfo(Severity.Create | Severity.Harmony | Severity.Event, "Patch: Ability Groups", "press shift while dragging abilities or spells to create foldable categories", false)]
    [HarmonyPatch]
    public class Patch_AbilityGroups : IUnitCommandActHandler
    {
        /* 
         * Notes:
         * - ActionBarVM: singular instance that holds everything (RootUIContext.Instance.InGameVM.StaticPartVM.ActionBarVM)
         *    ActionBarVM.GroupAbilities holds the VMs for the ability tab (generated by CollectAbilities)
         * - ActionBarSlotVM: primary instance of each slot
         *    ReactiveProperties Icon, ForeIcon, Name, DecorationSprite, DecorationColor, ResourceCount, ..
         * - ActionBarConvertedVM: child instance for foldable box; only one per ActionBarSlotVM; holds List<ActionBarSlotVM>
         * - MechanicActionBarSlot: child of ActionBarSlotVM; holds type depending logic
         * - ActionBarBaseSlotView: MonoBehaviour
         * - ActionBarConvertedView: MonoBehaviour
         * - ActionBarPCView
         * - ActionBarSlotPCView
         * 
         * - ActionBarSlot: related to inventory/spellbook only
         * - ActionBarSlots
         * - ActionBarGroupSlot
         * - ActionBarIndexSlot
         */

        public static ActionBarVM RootVM => RootUIContext.Instance.InGameVM.StaticPartVM.ActionBarVM;
        public static ActionBarPCView RootPCView => (RootUIContext.Instance.m_UIView as InGamePCView)?.m_StaticPartPCView?.m_ActionBarPCView;

        static Patch_AbilityGroups()
        {
            Reload();
            DefGroup.GroupBorder = Helper.CreateSprite(Path.Combine(Main.ModPath, "icons", "border_fancy3.png"));
        }

        public static void Reload()
        {
            try
            {
                DefGroup.Groups = Helper.Deserialize<HashSet<DefGroup>>(path: Path.Combine(Main.ModPath, "DefGroups.json"));
            } catch (Exception e1)
            {
                Main.PrintException(e1);
                try
                {
                    DefGroup.Groups = Helper.Deserialize<HashSet<DefGroup>>(value: DefaultDef);
                    Save();
                } catch (Exception e2)
                {
                    Main.PrintException(e2);
                    DefGroup.Groups = new();
                }
            }
        }

        public static void CollectFromResource(string guid, string title, string description, string icon)
        {
            var group = new DefGroup(title, description, icon);
            var guid2 = BlueprintGuid.Parse(guid);

            foreach (var ab in ResourcesLibrary.BlueprintsCache.m_LoadedBlueprints.Where(w =>
            {
                if (w.Value.Blueprint is BlueprintScriptableObject bp)
                {
                    if (bp.GetComponent<AbilityResourceLogic>()?.m_RequiredResource?.Guid == guid2)
                        return true;
                    if (bp.GetComponent<ActivatableAbilityResourceLogic>()?.m_RequiredResource?.Guid == guid2)
                        return true;
                }
                return false;
            }))
                group.Guids.Add(ab.Value.Blueprint.AssetGuid);
            if (DefGroup.Groups == null)
                DefGroup.Groups = new();
            DefGroup.Groups.Add(group);

            //var group = new DefGroup(title, description, icon);
            //var resource = BlueprintGuid.Parse(guid);
            //foreach (var ab in Resource.Cache.Ability.Where(w => w.GetComponent<AbilityResourceLogic>()?.m_RequiredResource?.Guid == resource)) // !w.HasVariants &&
            //    group.Guids.Add(ab.AssetGuid);
            //foreach (var ab in Resource.Cache.Activatable.Where(w => w.GetComponent<ActivatableAbilityResourceLogic>()?.m_RequiredResource?.Guid == resource))
            //    group.Guids.Add(ab.AssetGuid);
            //DefGroup.Groups.Add(group);
        }

        public static void AddGroup(string title, string description, string icon, params string[] guids)
        {
            if (title == null)
                return;

            var group = DefGroup.Groups.FindOrDefault(f => f.Title == title);
            if (group == null)
                DefGroup.Groups.Add(group = new(title, description ?? "", icon));

            foreach (var guid in guids)
            {
                var bguid = BlueprintGuid.Parse(guid);
                if (!group.Guids.Contains(bguid))
                    group.Guids.Add(bguid);
            }
            DefGroup.RefreshUI();
            Save();
        }

        public static void RemoveGroup(string title)
        {
            DefGroup.Groups.RemoveWhere(w => w.Title == title);
            DefGroup.RefreshUI();
            Save();
        }

        public static void Save()
        {
            try
            {
                Helper.Serialize(DefGroup.Groups, path: Path.Combine(Main.ModPath, "DefGroups.json"));
            } catch (Exception e) { Main.PrintException(e); }
        }

        public static void ToggleLocked()
        {
            DefGroup.Unlocked = !DefGroup.Unlocked;
            DefGroup.RefreshUI();
        }

        [HarmonyPatch(typeof(ActionBarVM), nameof(ActionBarVM.CollectAbilities))]
        [HarmonyPostfix]
        private static void CollectAbilities(UnitEntityData unit, ActionBarVM __instance)
        {
            if (DefGroup.Groups == null)
                return;

            foreach (var group in DefGroup.Groups)
            {
                int hash = group.GetHashCode();

                // find all abilities that match group and extract them
                var dic = new Dictionary<int, MechanicActionBarSlot>();
                for (int i = __instance.GroupAbilities.Count - 1; i >= 0; i--)
                {
                    var slot = __instance.GroupAbilities[i].MechanicActionBarSlot;
                    BlueprintGuid guid = DefGroup.GetGuid(slot);
                    if (guid == BlueprintGuid.Empty)
                        continue;

                    int index = group.Guids.IndexOf(guid);
                    if (index >= 0)
                    {
                        dic[index] = slot;
                        __instance.GroupAbilities[i].Dispose();
                        __instance.GroupAbilities.RemoveAt(i);
                    }
                }

                // fill unavailable abilities with placeholders
                if (DefGroup.Unlocked)
                {
                    for (int i = 0; i < group.Guids.Count; i++)
                        if (!dic.ContainsKey(i))
                            dic[i] = new MechanicActionBarSlotPlaceholder(unit, new BlueprintUnitFactReference() { deserializedGuid = group.Guids[i] });
                }

                // add group to actionbar
                var list = dic.OrderBy(o => o.Key).Select(s => s.Value).ToList(); // keep list in order of guids in the settings
                if (list.Count > 0 || DefGroup.Unlocked)
                    __instance.GroupAbilities.Add(new ActionBarSlotVM(new MechanicActionBarSlotGroup(unit, hash, list)));

                // update existing toolbar slots
                foreach (var slot in __instance.Slots)
                {
                    if (slot.MechanicActionBarSlot is MechanicActionBarSlotGroup mechanic && mechanic.GetHashCode() == hash)
                        mechanic.Slots = list;
                }

                //Helper.PrintDebug($"CollectAbilities Hash={hash} Title={group.Title} LCount={list.Count}");
            }
        }

        [HarmonyPatch(typeof(ActionBarSlotVM), nameof(ActionBarSlotVM.OnMainClick))]
        [HarmonyPostfix]
        private static void OnClick(ActionBarSlotVM __instance)
        {
            if (__instance.MechanicActionBarSlot is MechanicActionBarSlotGroup)
                __instance.OnShowConvertRequest();
        }

        [HarmonyPatch(typeof(ActionBarVM), nameof(ActionBarVM.OnUnitChanged))]
        [HarmonyPrefix]
        private static void KeepOpen1(ActionBarVM __instance, out int __state)
        {
            __state = -1;
            try
            {
                foreach (var slot in __instance.Slots)
                {
                    if (slot.ConvertedVm?.Value != null && slot.Index >= 0 && slot.MechanicActionBarSlot is IMechanicGroup)
                    {
                        __state = slot.Index;
                        break;
                    }
                }
            } catch (Exception e) { Main.PrintException(e); }
        }

        [HarmonyPatch(typeof(ActionBarVM), nameof(ActionBarVM.OnUnitChanged))]
        [HarmonyPostfix]
        private static void KeepOpen2(ActionBarVM __instance, int __state)
        {
            try
            {
                if (__state < 0)
                    return;

                foreach (var slot in __instance.Slots)
                {
                    if (slot.Index == __state && slot.MechanicActionBarSlot is IMechanicGroup)
                    {
                        Main.PrintDebug("reopend OnShowConvertRequest after refresh");
                        slot.OnShowConvertRequest();
                        break;
                    }
                }
            } catch (Exception e) { Main.PrintException(e); }
        }

        private static void SwapSlot(ActionBarSlotVM slot1, ActionBarSlotVM slot2)
        {
            var mechanic1 = slot1.MechanicActionBarSlot;
            var mechanic2 = slot2.MechanicActionBarSlot;
            SetSlot(slot1, mechanic2);
            SetSlot(slot2, mechanic1);
        }

        private static void SetSlot(ActionBarSlotVM target, MechanicActionBarSlot mechanic = null)
        {
            var unit = target.MechanicActionBarSlot.Unit;
            if (unit == null)
                return;

            int index = target.Index;
            if (index < 0)
            {
                var slots = unit.UISettings.m_Slots;
                for (index = 0; index < slots.Length; index++)
                {
                    if (slots[index] is MechanicActionBarSlotEmpty)
                        break;
                }
                if (index >= slots.Length)
                    return;
            }

            unit.UISettings.SetSlot(mechanic ?? new MechanicActionBarSlotEmpty() { Unit = unit }, index);
            RootVM.SetMechanicSlots(unit);
        }

        //[HarmonyPatch(typeof(ActionBarSlotPCView), "UnityEngine.EventSystems.IDragHandler.OnDrag")]
        //[HarmonyPostfix]
        private static void DragSlot2(PointerEventData data)
        {
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                return;

            var dragslot = RootPCView?.m_DragSlot?.gameObject;
            if (dragslot == null || !dragslot.activeSelf)
                return;

            ActionBarSlotVM targetSlot = data.pointerEnter.GetComponentInParent<ActionBarBaseSlotView>()?.ViewModel;
            var rect = data.pointerEnter.GetComponentInParent<RectTransform>();
            bool hit = RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out var localPosition);
            bool placeRight = localPosition.x >= 0;

            // TODO: highlight left/right placement
        }

        [HarmonyPatch(typeof(ActionBarSlotPCView), "UnityEngine.EventSystems.IEndDragHandler.OnEndDrag")]
        [HarmonyPrefix]
        private static bool DragSlot(PointerEventData eventData, ActionBarSlotPCView __instance)
        {
            /* 
             * combinations:
             * item     item    -> new group (spell group must target bar)
             * item     group   -> add to group
             * item     sub     -> add to group at index
             * item     void    -> ---
             * 
             * group    item    -> ---
             * group    group   -> ---
             * group    sub     -> ---
             * group    void    -> delete group
             * 
             * sub      item    -> new group (spell group must target bar)
             * sub      group   -> ---
             * sub      sub     -> swap index
             * sub      void    -> remove from group
             * 
             * InGameConsoleView
             * 
             */
#if VERBOSE
            try
            {
                int count = 0;
                Transform prev = null;
                Transform curr = eventData.pointerEnter?.transform;
                while (prev != curr && curr != null)
                {
                    var comps = curr.GetComponents<Component>();
                    Main.PrintDebug($"DragSlot:{count}: {comps.Join(f => $"{f.GetType().Name} {(f.transform as RectTransform)?.rect.center}")}");
                    prev = curr;
                    curr = curr.parent;
                    count++;
                }
            }
            catch (Exception)
            {
            }
#endif
            if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
                return true;
            if (DefGroup.Groups == null)
                return true;

            try
            {
                // clear drop indicator
                var dragslot = RootPCView?.m_DragSlot;
                if (dragslot != null)
                {
                    dragslot.Bind(null);
                    dragslot.gameObject.SetActive(false);
                }

                ActionBarSlotVM sourceSlot = __instance.ViewModel;
                ActionBarSlotVM targetSlot = eventData.pointerEnter?.GetComponentInParent<ActionBarBaseSlotView>()?.ViewModel;
                IMechanicGroup sourceParent = sourceSlot is ActionBarSlotVMChild vm1 ? vm1.Parent.MechanicActionBarSlot as IMechanicGroup : null;
                IMechanicGroup targetParent = targetSlot is ActionBarSlotVMChild vm2 ? vm2.Parent.MechanicActionBarSlot as IMechanicGroup : null;
                MechanicActionBarSlot sourceMechanic = sourceSlot.MechanicActionBarSlot;
                MechanicActionBarSlot targetMechanic = targetSlot?.MechanicActionBarSlot;
                var a1 = DefGroup.GetBlueprint(sourceMechanic);
                var a2 = DefGroup.GetBlueprint(targetMechanic);

                Main.PrintDebug($"DragSlot sourceSlot={sourceSlot.Index} targetSlot={targetSlot?.Index} sourceParent={sourceParent} targetParent={targetParent} sourceMechanic={sourceMechanic} targetMechanic={targetMechanic} a1={a1} a2={a2}");

                if (targetMechanic == null) // remove
                {
                    if (sourceMechanic is MechanicActionBarSlotGroup m1) // remove whole group
                        Helper.ShowMessageBox("Remove group?", onYes: () => RemoveGroup(m1.GetTitle()));
                    else
                        sourceParent?.RemoveFromGroup(sourceMechanic); // remove single ability
                }

                else if (targetParent != null) // add at specific index
                {
                    var rect = eventData.pointerEnter.GetComponentInParent<RectTransform>();
                    bool hit = RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out var localPosition);
                    var normalizedPosition = Rect.PointToNormalized(rect.rect, localPosition);
                    bool placeRight = localPosition.x >= 0;
                    Main.PrintDebug($"position right={placeRight} hit={hit} local={localPosition} normalized={normalizedPosition} rect={rect?.rect} mouse-x={eventData.position.x}");
                    targetParent.AddToGroup(sourceMechanic, targetMechanic, placeRight);
                    if (targetParent is MechanicActionBarSlotGroup)
                        Save();
                }

                else if (targetMechanic is IMechanicGroup m2) // add to group
                {
                    m2.AddToGroup(sourceMechanic);
                    if (targetMechanic is MechanicActionBarSlotGroup)
                        Save();
                }

                else if (a1 && a2) // add new group
                {
                    Helper.ShowInputBox("Create new group: ", onOK: a => AddGroup(a, "", null, a1.AssetGuid.ToString(), a2.AssetGuid.ToString()));
                }

                else if (DefGroup.IsValidSpellGroup(sourceMechanic) && DefGroup.IsValidSpellGroup(targetMechanic)) // add new spell group
                {
                    SetSlot(targetSlot, new MechanicActionBarSlotSpellGroup(targetMechanic.Unit, new List<MechanicActionBarSlot>() { sourceMechanic, targetMechanic }));
                }
            } catch (Exception e) { Main.PrintException(e); }
            return false;
        }

        #region Events

        public void HandleUnitCommandDidAct(UnitCommand command)
        {
            try
            {
                if (command is UnitUseAbility useAbility)
                {
                    var unit = command.Executor;
                    if (unit != null && unit.IsPlayerFaction && unit.Brain.IsAutoUseAbility(useAbility.Ability) && unit.UISettings.m_Slots != null)
                    {
                        foreach (var slot in unit.UISettings.m_Slots)
                        {
                            if (slot is MechanicActionBarSlotSpellGroup group && group.UpdateAutoUse())
                                break;
                        }
                    }
                }
            } catch (Exception e) { Main.PrintException(e); }
        }

        #endregion

        public static readonly string DefaultDef = "[{\"Title\":\"Kinetic Blades\",\"Description\":\"Kinetic Blades\",\"Icon\":\"41e9a0626aa54824db9293f5de71f23f\",\"Guids\":[\"89acea313b9a9cb4d86bbbca01b90346\",\"55790f1d270297f4a998292e1573a09e\",\"98f0da4bf25a34a4caffa6b8a2d33ef6\",\"4005fc2cd91860142ba55a369fbbec23\",\"371b160cbb2ce9c4a8d6c28e61393f6d\",\"37c87f140af6166419fe4c1f1305b2b8\",\"77d9c04214a9bd84bbc1eefabcd98220\",\"b9e9011e24abcab4996e6bd3228bd60b\",\"41e9a0626aa54824db9293f5de71f23f\",\"3f68b8bdd90ccb0428acd38b84934d30\",\"cf1085900220be5459273282389aa9c2\",\"ea2b3e7e3b8726d4c94ba58118749742\",\"5639fadad8b45e2418b356327d072789\",\"acc31b4666e923b49b3ab85b2304f26c\",\"dc6f0b906566aca4d8b86729855959cb\",\"66028030b96875b4c97066525ff75a27\",\"287e0c88af08f3e4ba4aca52566f33a7\",\"70524e9d61b22e948aee1dfe11dc67c8\"]},{\"Title\":\"Demon Aspects\",\"Description\":\"Demon Aspects and other demon related abilities.\",\"Icon\":\"e24fbd97558f06b45a09c7fbe7170a55\",\"Guids\":[\"0b57876f5dbbc784186b8b1f7d678602\",\"7b63a532fe1ad654fa1aa8f5ebf3cefb\",\"b17352531cb25d64fbf4078b856383c5\",\"9365979e813d90f4db1579dd36f0a3c9\",\"375089aeb3bcfa4479de8476b1589996\",\"fd1669c290212484894bc276d79bc63f\",\"745734402784ef34894aac64e35d46f0\",\"a693ad7d3783f8a4680ab410d9858525\",\"7a6f84f3df641d64e8f59e8fccf00568\",\"cf6355be6d63541418279a560039a866\",\"54d981871c4241844b7dcfc5d4893025\",\"b968988d6c0e830458fd49efbfb86202\",\"e24fbd97558f06b45a09c7fbe7170a55\",\"3070984d4c8bd4f48877337da6c7535d\",\"e642444d21a4dab4ea07cd042e6f9dc0\",\"49e1df551bc9cdc499930be39a3fc8cf\",\"55c6e91192b92b8479fa66d6aee33074\",\"37bfe9e5535e54c49b248bd84305ebd5\",\"868c4957c5671114eaaf8e0b6b55ad3f\",\"e305991cb9461a04a97e4f5b02b8b767\",\"fae00e8f4de9cd54da800d383ede7812\",\"0d817aa4f8bc00541a43ea2f822d124b\",\"8a474cae6e2788a498f616d36b56b5d2\",\"b6dc815e86a12654eb7f78c5f14008df\",\"600cf1ff1d381d8488faed4e7fbda865\",\"df9e7bbc606b0cd4087ee2d08cc2c09b\"]},{\"Title\":\"Metakinesis\",\"Description\":\"Metakinesis\",\"Guids\":[\"2dc9630110d0434ba7df785777b67be7\",\"c93c0217b3e0b4441a4f789dfb95fc8b\",\"b65ad9782f697f245baeb90cd5670546\",\"bb4369a9be4406147ad4f1a1f05adccf\",\"5c9f2b38404f118479a44234777e1ea8\",\"990dd3388df6a8d4cad1429380e71853\",\"53c7b6accfa1e8e4eba7004b17f61ac1\",\"031d823e0b804b3c868bd031e539cac3\"]},{\"Title\":\"Infusions\",\"Description\":\"Infusions\",\"Guids\":[\"6d35b4f39de9eb446b2d0a65b931246b\",\"88c37d8a7d808a844ba0116dd37e4059\",\"96b3fc11991f2664080c7c5e41417f48\",\"fb426ea002abbbc4198b1cd6b99f1be8\",\"abf5c26910fda5949abbc285c60416f9\",\"091b297f43ac5be43af31979c00ade57\",\"323be9d573657374da4e3f1456a2366c\",\"d0007fed20710ae4a96cebd2ba99f08b\",\"2816fad233e15a54c86729cee6e8969d\",\"e2e3ce12bdfc9d14a9ca4d51696dc8db\",\"b2d91bac690b74140b4fa3eec443edee\",\"06e3ac0ec6341744eb87f1f70a11576b\",\"bc5665a318bc4eb46a0537455509851a\",\"097c209e378144045ab97f4d54876959\",\"db3ccc72faeac0343891ba71bb692a42\",\"59303d0eb693cd2438fc89f91e29ab19\",\"30c81aff8e5293d418759d10f193f347\"]},{\"Title\":\"Metamagic Rods\",\"Description\":\"Metamagic Rods\",\"Icon\":\"485ffd3bd7877fb4d81409b120a41076\",\"Guids\":[\"ccffef1193d04ad1a9430a8009365e81\",\"cc266cfb106a5a3449b383a25ab364f0\",\"c137a17a798334c4280e1eb811a14a70\",\"78b5971c7a0b7f94db5b4d22c2224189\",\"5016f110e5c742768afa08224d6cde56\",\"fca35196b3b23c346a7d1b1ce20c6f1c\",\"cc116b4dbb96375429107ed2d88943a1\",\"f0d798f5139440a8b2e72fe445678d29\",\"056b9f1aa5c54a7996ca8c4a00a88f88\",\"ed10ddd385a528944bccbdc4254f8392\",\"605e64c0b4586a34494fc3471525a2e5\",\"868673cd023f96945a2ee61355740a96\",\"485ffd3bd7877fb4d81409b120a41076\",\"5a87350fcc6b46328a2b345f23bbda44\",\"b8b79d4c37981194fa91771fc5376c5e\",\"7dc276169f3edd54093bf63cec5701ff\",\"66e68fd0b661413790e3000ede141f16\",\"afb2e1f96933c22469168222f7dab8fb\",\"6cc31148ae2d48359c02712308cb4167\",\"077ec9f9394b8b347ba2b9ec45c74739\",\"69de70b88ca056440b44acb029a76cd7\",\"3b5184a55f98f264f8b39bddd3fe0e88\",\"9ae2e56b24404144bd911378fe541597\",\"1f390e6f38d3d5247aacb25ab3a2a6d2\",\"f0b05e39b82c3be408009e26be40bc91\",\"04f768c59bb947e3948ce2e7e72feecb\"]},{\"Title\":\"Druid Wildshapes\",\"Description\":\"Druid Wildshapes\",\"Guids\":[\"2f38f491888c89140969a1dc7af8c66e\",\"2d75f450494835f4294c9382e7a0cca7\",\"6d7fbb0866c374249b65f41c4c09c668\",\"90193ff4834295c4ca8b7ae0b3bd5215\",\"ac8811714a45a5948b27208538ce4f03\",\"92c47b04f6c9aa44abf1693b32554804\",\"23593d26c377ceb4fb6ecf66ef2d30c3\",\"758cf8a620d9caa4b9b30346b8c2a452\",\"de0f92fc1701dbe449fd18532aa15ced\",\"83122c16b894e6c4895d67c42a132ba1\",\"7de455201e2341f48ae9858e35dd9b31\",\"32f1f208ad635224f89ef158140ab509\",\"943d41b6aaef1dc4e82f115118dbf902\",\"b121d85600fc51e4f96c8527a20cd4e8\",\"392306525b8c4444cb73a5f24da971d3\",\"9e2f4e723f3f467419df4ef960494f16\",\"cd202b28ea83e1f40b633bede9c1c452\",\"e3ee03c3a959ca046a39827508ab8943\",\"e49d2cde42f25e546826600d11b4833e\",\"90aa5552c8db06045b1303de6ec7b627\",\"621cc9c46f5961b47adda27791e41f75\"]},{\"Title\":\"Raging Songs\",\"Description\":\"Raging Songs\",\"Guids\":[\"82f83a21ecbf9344d939c757152f5621\",\"f94731bf51da447f9aa214d260c9daae\",\"3b2ffac0776e4b7fb8c590b4ed4ad412\",\"fa159709a73d406fbb7a3bc74f102a4e\",\"2aa2ee37286e2f94083f6a36cf40c17f\",\"e7fef5dae0700cd428e92a9d049c6cf3\",\"d44831f2c5b12e8449df1e5dcab0fe66\",\"80e0c9d9cbcdc2c49a8b59560e049d47\",\"d928fdebd9958a746a15d01334305d19\",\"33f957216dfccc8458f8bb048ae74c71\",\"5860ff292cd73f7438049b175bbefdd9\",\"44bfb3d43e68de84e9b5cef8defd2bfb\",\"264e93ac44ace16488226b8f7756bf26\",\"334cb75aa673d1d4bb279761c2ef5cf1\",\"e8e5a5cc8b603d5448d9098cc20065f2\",\"c62bb8123ebc5874d9601ba02907afca\",\"bf606afc908e4f1a915f314d5fdfdbbc\",\"97ad1ef4b80640cebfabee1f794e157b\",\"cbffca886b884712a202f6bf68302b6c\"]},{\"Title\":\"Ki Powers\",\"Description\":\"\",\"Guids\":[\"7e82395f05961e14cbc14a75d3a94f0f\",\"fd268041665a99f469b979046a463e2d\",\"59ff834c7b4452a48ba8853f0e235f2c\",\"03e1b646dd9edde4b8d72cea74a7f820\",\"e7e4c7a4368a2ca40b6a89d4c4a1f275\",\"0c7c566cdaa0468489d0f4d6edd6cde3\",\"dacf302d91f32234aba6b95f35e39ba5\",\"e01a15767bdfb334b828a5a4298679a6\",\"66a86834a98b15544b5c1b66328083a8\",\"7c4ed296557c0414dbace78fcc415e46\",\"6c2f17e3aecf4304bb1d3bc60cc9d9f3\",\"e10a3ddcacc8370419022c89df3d9e80\",\"ddfec2580a9fda1458efe75fbbd1a9a2\",\"502d92213efc1dd4a8a322ce6d7a95b5\",\"194910d95cdcc9b4b987c368a6467cc6\",\"0b129e96521e54e44b061df1ddb3b486\",\"8ddfed5bc95560e4d83756c8e5e2c33d\",\"af6aa2b6398b8a749898fd71175d73f2\",\"62071f9b0fada4f459a96ee0457745a3\",\"6f4bc76e64e557745a1fb04d958a65fe\",\"9615ccc21a817e8418062a49eae51b8b\",\"dff4b9cb0b24c9842a971c4176088f11\",\"c6484ec9a9d805a44a33281fd4652998\",\"6dc830b2210ec944aa407fd024b3d3b3\",\"15029d70cc507ae4cb480a7092775302\",\"282411eed5f0fbe4eb4c1b01cc80c0a5\",\"b53ee7514922ab84581063ab3bb4ed2a\",\"336a841704b7e2341b51f89fc9491f54\",\"6dc679bdc40092e4c86933c337481a0f\",\"b1a1b7d59e4b6c44cb4e622baa171eb6\",\"7f6ea312f5dad364fa4a896d7db39fdd\",\"d4b5f47fbe1074d4e9127dd08f21abda\",\"4de518e69f9b8094fb996b1599d00314\",\"81d2a0b908e7bb74790a6ecdf5795a69\",\"450a8d492a3342742917c3a3b357f25e\",\"6e423d7de48eef74b999ebc8a62df8b8\",\"a9e6e105350562f45b41a8ebea1d8d87\",\"8c98b8f3ac90fa245afe14116e48c7da\",\"61da79969661b1349b042aa99623439d\",\"f5f25b1319eef254f9197e71a969c03b\",\"166d350cb853cfe4ca24584e9206e294\",\"908b8f6173e0489eaddb95b8589ce322\"]},{\"Title\":\"Ki Powers Scaled Fist\",\"Description\":\"\",\"Guids\":[\"45fe18c8dcd11e54e8499646b9389029\",\"9bfed63e0b9bfa1478c521f0527fb772\",\"f0bd350c96848364d8c8f7d3167499e9\",\"38b32df1eb0aa45409b114ed345a1631\",\"0019e0810a828a049b4c37a7effa2385\",\"625958680ce6de844a996bde77c99e5b\",\"cf128ce20d9ebdc46804e471b24ce71c\",\"aec48af6d05577248a91b69ed843e4cf\",\"b08e7d0bdd3830f4f8292668b1ffac2e\",\"5a2accb17ffde8b4dafb8c8f7e00b711\",\"0ad72a265688d3c40bc7c967801e056f\",\"9ae37b34f8d9dbc42a9651fc16465c99\",\"e5d62d1a0deea44489c659c63d5b2682\",\"a405b9c06947ac24f84ec044e869919c\",\"838b3be2249b33e4d96a0392132d1604\",\"0a1c08c4bfa268d45a3553008878f20d\",\"578ad0a7bfa144c4cbfcbc641e97cf9d\",\"ca948bb4ce1a2014fbf4d8d44b553074\",\"3a9d9ca38885fb144a766f0ea5962e98\",\"749e77f7014cb4e4487400e508e70a59\",\"02c38239b97f44b4bb9d83e4352d76f9\",\"1b95baefa8931574aa15a579e4423063\",\"e6b2906f33e7abe4394c053465563353\",\"3fdcebb333e8f394099ddc105993e85a\",\"f91f00c3f59d63348a908195ee6d9e56\",\"2719c3185b6c3e946bfcdb788ae9adc6\",\"f3054941690b5b84986e6a65c7037e50\"]},{\"Title\":\"Arcane Pool\",\"Description\":\"\",\"Guids\":[\"1b7fb8120390ca24c9da98ce87780b7f\",\"cf7c4eaa2b47d7242b2c734df567cefb\",\"8b425e230a6224448bc30682ee596ae3\",\"fa12d155c229c134dbbbebf0d7b980f0\",\"3c89dfc82c2a3f646808ea250eb91b91\",\"0ba39a85cda2441e95d9022413699cd2\",\"3cea97ce6c16a9e4b87c972c981432c1\",\"1bd76e00b6e056d42a8ecc1031dd43b4\",\"b2063ca51a463b5499d8f080b7adb819\",\"5a169c57935dc3343836c027e35d65b3\",\"c6559839738a7fc479aadc263ff9ffff\"]},{\"Title\":\"Lay On Hands\",\"Description\":\"\",\"Guids\":[\"8337cea04c8afd1428aad69defbfc365\",\"c8feb35c95f9f83438655f93552b900b\",\"7ed6c1501a8c7224bac037dcee4b6705\",\"64351f4936663d64bb0ab30e5902d45e\",\"1bcef5aeeee69944b882718474ea53e2\",\"1785979af3db14f428a8a70943ea3852\",\"c89c4918529156a4d8636ed9060dc5cd\",\"4937473d1cfd7774a979b625fb833b47\",\"6670f0f21a1d7f04db2b8b115e8e6abf\",\"caae1dc6fcf7b37408686971ee27db13\",\"8d6073201e5395d458b8251386d72df1\",\"d0b5efbe5bc5499cb0ce0115a1b09e3c\"]},{\"Title\":\"Aeon Gaze\",\"Description\":\"\",\"Guids\":[\"3e2d25b97be14414b897fc97f2d76c9a\",\"12926320b39f4b708605f37cd3703ea3\",\"79d287eefba48424b9073a5cabba2a63\",\"6fbcc3bba92622145bb95a492ad61966\",\"812f8ea042924b60a564ea4f192740a9\",\"0216d48d09c13314e9a88021766d6535\",\"0de63f0b4b2a44a7babdd385f770a550\",\"e5054383e6b583e4e90b84bf148126fb\",\"9048a33226be4af458e126836604e9a0\",\"6a40982a487b4c088d3669cb3c4280b3\",\"d858112219817be4b8a4bb55f3e62e2a\",\"f5c1d8aa47da4fb0b7681a82d5d9d3f4\",\"a73ae0e333214c5f8fdfb20d6f5cde0f\",\"6eaafc0aad094a7785d952add51ba4d0\",\"47e43de6652649da8f0a2f4bd3b554a6\",\"3cb1df32514b4cd4a3a6718c75f09e96\",\"116a7837b2ac46d59a02ad24d8c7a568\",\"b1c7a7bdc0f45e8468fcc600b50d906e\",\"ef12f84ec3434ebaa9a15f6a160572b9\"]},{\"Title\":\"Warpriest Fervor\",\"Description\":\"\",\"Guids\":[\"b5cf6b80e65ea724d99dc9f4f8874fc3\",\"023bd78a3b068e84aad2b1fd273daae1\",\"67d9c0b9a38df2d47b5156748b36be5a\",\"fd5d66b9b64cca6499d4d17af0f5577c\",\"df91f64952884c56a163b4c511462e86\",\"b1f39dcf9fbcc8f49a7b5761bbfc27f6\",\"608a63ea6eec40bd8598c76965bf439c\",\"5542b984ed4e7a74eac305d3c2413e1d\",\"a2736145a29c8814b97a54b45588cd29\",\"cdd09c9059e068040914b5f242e98720\",\"a5560d120fe4a234688842bbb7fc01bb\",\"a24dc2a5900893047907b83ea3023abc\",\"25a89a7bc31e5ee4e820d89d875b6a1c\",\"4b4a148f99a51cd4f948965400a79078\",\"c35a72647e2b0494ba97f8394fd13f5f\",\"74067bbe5f3b45f439fab7c2ae9eaa54\",\"231588abe1b71804da5ae146f2f999ea\",\"ec6e3a6bef4788849a63aa7a9db46cfe\",\"3135fd7b497c01c4f9348bfa7a4e71bb\",\"f354d4c7102c28342a7412867deb9845\",\"6bcaf7636388f2a40bce263372735eef\",\"d5f525a9c5efa634d843d5c35f074f02\",\"646f8dfdef14db747a093b7bf146c269\",\"894e20539c353c74ab2733a056351947\"]},{\"Title\":\"Stunning Fist\",\"Description\":\"\",\"Guids\":[\"32f92fea1ab81c843a436a49f522bfa1\",\"c81906c75821cbe4c897fa11bdaeee01\",\"7e87f6e176b28d54a98b3490f8cba9db\",\"957a10e303269324dbf1a70513f37559\",\"732ae7773baf15447a6737ae6547fc1e\"]},{\"Title\":\"Bardic Performance\",\"Description\":\"\",\"Guids\":[\"584427c2d3d3c5d45a169b82431612bc\",\"c182f1450932c634e954148a9b16618d\",\"9b7fa6cadc0349449829873c63cc5b0b\",\"12dc796147c42e04487fcad3aaa40cea\",\"08cc564a5e2c49e4b8eefef393d8041c\",\"406e8baa9e223d14ca981ee3e80426d1\",\"1b28d456a5b1b4744a1d87cf24309ad1\",\"32d247b6e6b65794ab47fc372c444a96\",\"b1d8fdffd132bfd428a8045b7b8b363c\",\"dbd7c54ba43e1d54592e037d63117f7b\",\"d5ee8a2e5bf46c549988e9b09a59acd4\",\"1bf09d2956d0f7a4eb4f6a2bcfb8970f\",\"d99d63f84e180d44e8f92b9a832c609d\",\"993908ad3fb81f34ba0ed168b7c61f58\",\"ad8a93dfa2db7ac4e85133b5e4f14a5f\",\"430ab3bb57f2cfc46b7b3a68afd4f74e\",\"5250fe10c377fdb49be449dfe050ba70\",\"be36959e44ac33641ba9e0204f3d227b\",\"a4ce06371f09f504fa86fcf6d0e021e4\",\"72e4699d1f86461429bf5c22866b5c4a\",\"70274c5aa9124424c984217b62dabee8\",\"29f4e6db62a482741b30fb548ce55598\",\"2ce9653d7d2e9d948aa0b03e50ae52a6\",\"c2b46413ce734405bbe7fbe92b92961f\",\"7dec1f71ee1dedf42a9444f4a260cc88\"]},{\"Title\":\"Arcanist Consume Spells\",\"Description\":\"\",\"Guids\":[\"46ac75e80be0cbd448ac48af6e75303f\",\"28b3a4bb27c8caf4a83b698381ff92f3\",\"3ddc1dee5b581324c9799b9113050da3\",\"da8ffa1776ba6624fb057d54cc01e5e7\",\"ad20ec369624c3840bedb23183b16348\",\"154154b1ae8b60b48b936e0666cd2c86\",\"30547ef6853a1c94abe3d0bfec78e1f9\",\"97704a2dc6ce7284ab950afc174e6292\",\"474539f907513cb408238ce31905d9a6\"]},{\"Title\":\"Arcanist Arcane Reservoir\",\"Description\":\"\",\"Guids\":[\"91295893ae9fdfb4b8936a93eff019df\",\"0b10e8506f321f54eb4a4b39dd18d959\",\"36f81ca4bf1066e48a96866a32329c19\",\"8e51fb7352b4fd54e8ebbfec9eef6237\",\"fff22537035652b48b9359b0a3de5f2e\",\"2d7d510c6e2e3e54ab9eee84a41fa2cf\",\"e81570b9df7758e4195346340231e6e3\",\"d340a8444b97a6e42a41dd8edc1c7ae5\",\"2c5738e476df39545b4972e8952ec37a\",\"dcb39ac1afdbc844ca7c308156ec6d7d\",\"9c256a05031ff8647afb72bf41fa97e9\",\"c65f8c39025c15245877d4d5d790019c\",\"84c6c7b1f5997b74bb40853ccf02fd87\",\"ac0886bd0db8ac940a223e0c54f5527a\",\"5d49f2ae3849ed744b90fc43d88930f8\",\"b6dad489bbf24474d883f421da64fdd7\",\"3ce3004516d1eb944bb9ba58a7cf49a2\",\"5eb94db25f3a330478e2b663cbb22a54\",\"7aa4ca655b8db434c9b09f4c431aa77e\",\"574d59bb28227ab408def8aa67c54387\",\"951d71f338f79944494b69d67fe9eff5\",\"74d0a98092a77324bb4de920029785df\",\"dcb0ec4cf1590ed418e7345c6b5b8ff2\",\"8450359f7f64dae42968088413a72961\",\"85f66c8af704f0448909a9d1d4b35e08\",\"475ad22964dc02943ae2f1a22aa579b7\",\"a45f3dae9c64ec848b35f85568f4b220\",\"749567e4f652852469316f787921e156\",\"39db93f906429eb4583fc2f9a1812665\",\"e6f50a07fc625a7439439293457c6eed\",\"009069c2b32c1ab43833668fcdda2b2e\",\"79b4e965225cc1d46b79ff4832b536b9\",\"3bd548e25a144f7d91ab2c239f455e10\",\"4eba085b83dd4f4c932bca86f8a79aa3\",\"69bd25353fda4d6193414c8763b10b1e\",\"1687104b61bc46f9ae97cd1f36d58de5\",\"f9002aefb21b48bab046194d20d4d581\",\"a8c8d3c4e1b24506ad3df6acf9ae3376\",\"ca2abec2c08f4989a0c826527b73c2c9\",\"5a90d9e5854c36e459de57318a45af97\",\"e84cb97373ca6174397bfe778a039eab\",\"22a5d013be997dd479c19421343cfb00\",\"83d6d8f4c4d296941838086f60485fb7\",\"d43c22896b9ef094fbd5b67689b5410e\",\"44cf8a9f080a23f4689b4bb51e3bdb64\",\"4d616f08e68288f438c8e6ce57672a56\",\"1203e2dab8a593a459c0cc688f568052\",\"586e964c75e0c6a46884a1bea3e05cdf\",\"fdc6aa23a730426ba4a70a41b76a8fe2\"]},{\"Title\":\"Alchemist Bombs\",\"Description\":\"\",\"Guids\":[\"a64a3f4c8370e3646ada9e140e28f7fb\",\"5fa0111ac60ed194db82d3110a9d0352\",\"fd101fbc4aacf5d48b76a65e3aa5db6d\",\"bd05918a568c41e49aed7b9526ba596b\",\"17041de68a89a5f4b92889c3ed475c00\",\"0fcbed386deb18b428825605796d5a15\",\"3ac7286a18ba6234a908ae5d8b84d107\",\"1fc8b4eccde6c9f49bef1dbd6d702547\",\"00376414ceff5d34dac42e2be8537cfd\",\"197624a197c10cb48bc4dcb229efb91b\",\"f80896af0e10d7c4f9454cf1ce50ada4\",\"2b76e3bd89b4fa0419853a69fec0785f\",\"557898e059f5ff644848b0a4df087391\",\"addf00b42747e1b47917b852073ddcd9\",\"b94ee802dc1574b4fb71215a4a6f11dc\",\"9aef2eb14fba66d47bef9442311e346e\",\"526aa6319e9174e4ab2026e0f299b011\"]}]";
    }
}
