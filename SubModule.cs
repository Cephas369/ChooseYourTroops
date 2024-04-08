using TaleWorlds.MountAndBlade;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using Helpers;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using System;
using System.Collections;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Library;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Localization;
using System.Reflection;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers;
using TaleWorlds.InputSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ScreenSystem;
using SandBox.View.Menu;

namespace ChooseYourTroops
{

    public class ChooseYourTroopsBehavior : CampaignBehaviorBase
    {
        private static BattleSideEnum playerSide = BattleSideEnum.None;
        private static TroopRoster actualTroopRoster = null;
         
        private static readonly Dictionary<int, int> battleSizes = new()
                { { 0, 200 }, { 1, 300 }, { 2, 400 }, { 3, 500 }, { 4, 600 }, { 5, 800 }, { 6, 1000 } };

        private static List<(FlattenedTroopRosterElement, MapEventParty, float)> actualreadyTroopsPriorityList =
            null;

        private static bool setupSide = false;
        private static bool haveChosenTroops = false;
        private static int PlayerArmySize;
        private static int EnemyArmySize;
        private static int initialPlayerSpawn;
        public override void RegisterEvents()
        {
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this,(IMission mission) => CheckTroopRoster());
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, CheckTroopRoster);
            CampaignEvents.OnPartySizeChangedEvent.AddNonSerializedListener(this, (PartyBase party) =>
                {
                    if(party==MobileParty.MainParty.Party && Mission.Current == null)
                        CheckTroopRoster();
                });
        }

        private static void CheckTroopRoster()
        {
            
            if (actualTroopRoster != null)
            {
                List<TroopRosterElement> partyRoster = MobileParty.MainParty.MemberRoster.GetTroopRoster();
                List<TroopRosterElement> actualRoster = actualTroopRoster.GetTroopRoster();
                actualTroopRoster.Clear();
                for (int i = 0; i < actualRoster.Count; i++)
                {
                    TroopRosterElement partyTroop = partyRoster.FirstOrDefault(x => x.Character.StringId == actualRoster[i].Character.StringId);
                    if (partyTroop.Equals(default(TroopRosterElement)) || partyTroop.Number == partyTroop.WoundedNumber || partyTroop.Number <=0)
                    {
                        actualRoster.Remove(actualRoster[i]);
                        continue;
                    }

                    if (actualRoster[i].Number > partyTroop.Number - partyTroop.WoundedNumber)
                    {
                        TroopRosterElement actualTroop = actualRoster[i];
                        actualTroop.Number = partyTroop.Number - partyTroop.WoundedNumber;
                        actualRoster[i] = actualTroop;
                    }

                }
                foreach (TroopRosterElement troop in actualRoster)
                {
                    actualTroopRoster.Add(troop);
                }
                
            }

        }
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("actualTroopRoster", ref actualTroopRoster);
        }


        [HarmonyPatch(typeof(EncounterGameMenuBehavior), "AddGameMenus")]
        public static class EncounterGameMenuBehaviorPrefix
        {
            private static bool isPlayerArmyBig()
            {
                if (PlayerEncounter.Battle != null && PlayerEncounter.Battle.IsFinalized &&
                    PlayerEncounter.Battle.IsFinished)
                    return false;
                if (MobileParty.MainParty.Army != null &&
                    MobileParty.MainParty.Army.LeaderParty.Name != MobileParty.MainParty.Name)
                    return false;
                if (PlayerEncounter.Battle != null && PlayerSiege.BesiegedSettlement == null &&
                    MobileParty.MainParty.Army == null &&
                    PlayerEncounter.Battle.PartiesOnSide(PlayerEncounter.Battle.PlayerSide).Count(x=>!x.IsNpcParty) > 1 &&
                    PlayerEncounter.Battle.GetLeaderParty(PlayerEncounter.Battle.PlayerSide).Name !=
                    PartyBase.MainParty.Name)
                    return false;
                if (PlayerEncounter.Battle != null || MapEvent.PlayerMapEvent != null ||
                    PlayerEncounter.EncounteredParty?.MapEvent != null)
                {
                    MapEvent encounteredBattle = PlayerEncounter.Battle ??
                                                 MapEvent.PlayerMapEvent ?? PlayerEncounter.EncounteredParty?.MapEvent;
                    playerSide = encounteredBattle.PlayerSide;
                    try
                    {
                        PlayerArmySize = encounteredBattle.GetMapEventSide(encounteredBattle.PlayerSide)
                            .GetTotalHealthyTroopCountOfSide();
                        EnemyArmySize = encounteredBattle.GetMapEventSide(encounteredBattle.PlayerSide.GetOppositeSide())
                            .GetTotalHealthyTroopCountOfSide();
                    }
                    catch(Exception)
                    {
                        return false;
                    }


                }
                else if (PlayerSiege.BesiegedSettlement != null)
                {
                    PlayerArmySize = PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(PlayerSiege.PlayerSide)
                        .GetInvolvedPartiesForEventType().Sum(x => x.NumberOfHealthyMembers);
                    EnemyArmySize = PlayerSiege.PlayerSiegeEvent
                        .GetSiegeEventSide(PlayerSiege.PlayerSide.GetOppositeSide())
                        .GetInvolvedPartiesForEventType().Sum(x => x.NumberOfHealthyMembers);


                    playerSide = PlayerSiege.PlayerSide;
                }
                else
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage(
                            "ChooseYourTroops Error: Enemy party or besieged settlement not found."));
                    return false;
                }


                return true;
            }

            [HarmonyPostfix]
            public static void Postfix(CampaignGameStarter gameSystemInitializer)
            {

                gameSystemInitializer.AddGameMenuOption("encounter", "select_troops",
                    "{=select_your_troops_text}Select your troops.".ToString(), delegate(MenuCallbackArgs args)
                    {
                        args.optionLeaveType = GameMenuOption.LeaveType.TroopSelection;
                        args.IsEnabled = true;
                        bool condition = isPlayerArmyBig();
                        if (PlayerArmySize <= 1)
                        {
                            args.IsEnabled = false;
                            args.Tooltip = new TextObject("{=select_your_troops_tooltip}There is only one person or less on your party.");
                        }
                        return condition;
                    }, new GameMenuOption.OnConsequenceDelegate(OpenTroopsSelector));

                gameSystemInitializer.AddGameMenuOption("menu_siege_strategies", "select_troops_menu_siege_strategies",
                    "{=select_your_troops_text}Select your troops.", delegate(MenuCallbackArgs args)
                    {
                        args.optionLeaveType = GameMenuOption.LeaveType.TroopSelection;
                        args.IsEnabled = true;
                        bool condition = isPlayerArmyBig();
                        if (PlayerArmySize <= 1)
                        {
                            args.IsEnabled = false;
                            args.Tooltip = new TextObject("{=select_your_troops_tooltip}There is only one person or less on your party.");
                        }
                        return condition;
                    }, new GameMenuOption.OnConsequenceDelegate(OpenTroopsSelector));
            }

            static bool CanChangeStatusOfTroop(CharacterObject character) => !character.IsPlayerCharacter;

            static void OnDone(TroopRoster troopRoster, MenuCallbackArgs args)
            {
                if (troopRoster.TotalManCount < initialPlayerSpawn)
                    initialPlayerSpawn = troopRoster.TotalManCount;
                if (!haveChosenTroops)
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=troops_selected_text}Troops selected!").ToString(), new Color(15, 70, 160)));
                actualTroopRoster = troopRoster;
                haveChosenTroops = true;
            }
            public static void OpenTroopSelection(MenuCallbackArgs args, TroopRoster fullRoster, TroopRoster initialSelections, Func<CharacterObject, bool> canChangeChangeStatusOfTroop, Action<TroopRoster> onDone, int maxSelectableTroopCount, int minSelectableTroopCount)
            {
                if (args.MenuContext.Handler != null)
                {
                    MenuViewContext menuView = args.MenuContext.Handler as MenuViewContext;
                    if (menuView != null)
                        AccessTools.Field(typeof(MenuViewContext), "_menuTroopSelection").SetValue(menuView, menuView.AddMenuView<CYTGauntletMenuTroopSelectionView>(fullRoster, initialSelections, canChangeChangeStatusOfTroop, onDone, maxSelectableTroopCount, minSelectableTroopCount));
                }
            }   
            private static void OpenTroopsSelector(MenuCallbackArgs args)
            {


                CheckTroopRoster();
                int initialPlayerTroops = PlayerArmySize;
                if (PlayerArmySize + EnemyArmySize > battleSizes[BannerlordConfig.BattleSize])
                {
                    if (PlayerArmySize > battleSizes[BannerlordConfig.BattleSize] / 2 ||
                        EnemyArmySize > battleSizes[BannerlordConfig.BattleSize] / 2)
                    {
                        if (EnemyArmySize < PlayerArmySize)
                        {
                            initialPlayerTroops = battleSizes[BannerlordConfig.BattleSize] / 2 +
                                                  (battleSizes[BannerlordConfig.BattleSize] / 2 - EnemyArmySize);
                        }

                        if (PlayerArmySize > battleSizes[BannerlordConfig.BattleSize] / 2 &&
                            EnemyArmySize > battleSizes[BannerlordConfig.BattleSize] / 2)
                            initialPlayerTroops = battleSizes[BannerlordConfig.BattleSize] / 2;
                    }
                }

                TroopRoster armyRoster = MobileParty.MainParty.MemberRoster;

                if (MobileParty.MainParty.Army != null &&
                    MobileParty.MainParty.Army.LeaderParty.Name == MobileParty.MainParty.Name)
                {
                    armyRoster = TroopRoster.CreateDummyTroopRoster();
                    foreach (MobileParty party in MobileParty.MainParty.Army.Parties)
                    {
                        armyRoster.Add(party.MemberRoster);
                    }
                }
                else if ((PlayerEncounter.Battle != null && PlayerEncounter.Battle.PartiesOnSide(playerSide).Count > 1) || (PlayerSiege.BesiegedSettlement != null &&
                    PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(PlayerSiege.PlayerSide).GetInvolvedPartiesForEventType().Count() > 1) && MobileParty.MainParty.Army == null)
                {
                    armyRoster = TroopRoster.CreateDummyTroopRoster();
                    List<PartyBase> PartiesOnPlayerSide = PlayerEncounter.Battle != null ? PlayerEncounter.Battle.PartiesOnSide(playerSide).Select(x => x.Party).ToList() :
                        PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(PlayerSiege.PlayerSide).GetInvolvedPartiesForEventType().ToList();
                    foreach (PartyBase party in PartiesOnPlayerSide)
                    {
                        armyRoster.Add(party.MemberRoster);
                    }
                }

                    initialPlayerSpawn = initialPlayerTroops;
                TroopRoster dummyTroopRoster = TroopRoster.CreateDummyTroopRoster();
                dummyTroopRoster.Add(armyRoster.ToFlattenedRoster().Where(x => x.Troop.IsHero && !x.IsWounded));
                if (actualTroopRoster != null)
                {
                    List<FlattenedTroopRosterElement> soldiers = actualTroopRoster.ToFlattenedRoster()
                        .Where(x => !x.Troop.IsHero && !x.IsWounded).ToList();
                    if (soldiers.Count + dummyTroopRoster.TotalManCount > initialPlayerTroops)
                        soldiers.RemoveRange(initialPlayerTroops - 1 - dummyTroopRoster.TotalManCount,
                            soldiers.Count + dummyTroopRoster.TotalManCount - initialPlayerTroops);
                    dummyTroopRoster.Add(soldiers);
                }
                else
                {
                    TroopRoster strongestAndPriorTroops =
                        MobilePartyHelper.GetStrongestAndPriorTroops(MobileParty.MainParty,
                            initialPlayerTroops - dummyTroopRoster.TotalManCount, false);
                    strongestAndPriorTroops.RemoveIf(x => x.Character.IsHero);
                    dummyTroopRoster.Add(strongestAndPriorTroops);
                }




                OpenTroopSelection(args, armyRoster, dummyTroopRoster,
                    new Func<CharacterObject, bool>(CanChangeStatusOfTroop),
                    delegate(TroopRoster roster) { OnDone(roster, args); }, initialPlayerTroops, 1);
            }

            public static Dictionary<UniqueTroopDescriptor, MapEventParty> allocatedTroops;

            [HarmonyPatch(typeof(MapEventSide), "AllocateTroops")]
            static class AllocateTroopsPrefix
            {

                [HarmonyPrefix]
                static void Prefix(ref List<UniqueTroopDescriptor> troopsList, ref int number,
                    ref Func<UniqueTroopDescriptor, MapEventParty, bool> customAllocationConditions,
                    MapEventSide __instance)
                {
                    try
                    {
                        if (setupSide && actualTroopRoster != null && !__instance.MapEvent.IsPlayerSimulation &&
                            number == initialPlayerSpawn)
                        {
                            if (__instance.MissionSide == playerSide && PlayerArmySize > 0)
                            {
                                allocatedTroops = new();
                                FlattenedTroopRoster flattenedTroopRoster = actualTroopRoster.ToFlattenedRoster();
                                List<(FlattenedTroopRosterElement, MapEventParty, float)> list =
                                    (List<(FlattenedTroopRosterElement, MapEventParty, float)>)AccessTools
                                        .Field(typeof(MapEventSide), "_readyTroopsPriorityList").GetValue(__instance);
                                actualreadyTroopsPriorityList = list.AsReadOnly().ToList();
                                customAllocationConditions = (descriptor, party) =>
                                {
                                    FlattenedTroopRosterElement troop = flattenedTroopRoster.FirstOrDefault(x =>
                                        x.Troop.StringId == party.Troops[descriptor].Troop?.StringId);
                                    if (!troop.Equals(default(FlattenedTroopRosterElement)))
                                    {
                                        allocatedTroops.Add(descriptor, party);
                                        flattenedTroopRoster.Remove(troop.Descriptor);
                                        return true;
                                    }

                                    return false;
                                };
                            }
                        }
                    }

                    catch (Exception e)
                    {
                        InformationManager.DisplayMessage(
                            new InformationMessage("ChooseYourTroops Error at AllocateTroops Prefix"));
                    }
                }
            }

            [HarmonyPatch(typeof(MapEventSide), "AllocateTroops")]
            static class AllocateTroopsPostfix
            {

                [HarmonyPostfix]
                static void Postfix(ref List<UniqueTroopDescriptor> troopsList, int number,
                    Func<UniqueTroopDescriptor, MapEventParty, bool> customAllocationConditions,
                    MapEventSide __instance)
                {
                    if (setupSide && __instance.MissionSide == playerSide && number == initialPlayerSpawn &&
                        actualTroopRoster != null)
                    {
                        try
                        {
                            actualreadyTroopsPriorityList.RemoveAll(
                                x => allocatedTroops.ContainsKey(x.Item1.Descriptor));
                            AccessTools.Field(typeof(MapEventSide), "_readyTroopsPriorityList")
                                .SetValue(__instance, actualreadyTroopsPriorityList);
                            AccessTools.Field(typeof(MapEventSide), "_allocatedTroops")
                                .SetValue(__instance, allocatedTroops);

                        }
                        catch (Exception e)
                        {
                            InformationManager.DisplayMessage(
                                new InformationMessage("ChooseYourTroops Error at AllocateTroops Postfix"));
                            actualreadyTroopsPriorityList = null;
                        }
                    }


                }
            }

            [HarmonyPatch(typeof(PlayerEncounter), "FinishEncounterInternal")]
            static class PlayerEncounterPrefix
            {
                [HarmonyPrefix]
                static void Prefix()
                {
                    playerSide = BattleSideEnum.None;
                    setupSide = false;
                    actualreadyTroopsPriorityList = null;
                    haveChosenTroops = false;
                }
            }

            [HarmonyPatch(typeof(MissionAgentSpawnLogic), "AfterStart")]
            static class AfterStartPostfix
            {
                [HarmonyPostfix]
                static void Postfix(MissionAgentSpawnLogic __instance)
                {
                    if (haveChosenTroops)
                    {
                        setupSide = true;
                    }
                }
            }

            [HarmonyPatch(typeof(LordsHallFightMissionController), "OnCreated")]
            static class InitializeMissionPrefix
            {
                [HarmonyPrefix]
                static void Prefix()
                {
                    if (setupSide)
                    {
                        setupSide = false;
                        haveChosenTroops = false;
                    }
                }
            }

            [HarmonyPatch(typeof(MissionAgentSpawnLogic), "OnBattleSideDeployed")]
            static class OnBattleSideDeployedPostfix
            {
                [HarmonyPostfix]
                static void Postfix(BattleSideEnum side, MissionAgentSpawnLogic __instance)
                {
                    if (side == playerSide)
                        PlayerArmySize = 0;
                    else
                        EnemyArmySize = 0;
                }
            }

            private static int EnemyInitialSpawn => EnemyArmySize <= battleSizes[BannerlordConfig.BattleSize] - initialPlayerSpawn ? EnemyArmySize : battleSizes[BannerlordConfig.BattleSize] - initialPlayerSpawn;


            [HarmonyPatch(typeof(MissionAgentSpawnLogic), "Init")]
            static class MissionAgentSpawnLogicInitdPostfix
            {

                [HarmonyPostfix]
                static void Postfix(bool spawnDefenders, bool spawnAttackers,
                    in MissionSpawnSettings reinforcementSpawnSettings, MissionAgentSpawnLogic __instance)
                {
                    try
                    {
                        if (haveChosenTroops)
                        {
                            var phases = __instance.GetType()
                                .GetField("_phases", BindingFlags.NonPublic | BindingFlags.Instance)
                                .GetValue(__instance);
                            object[] phasesArray = (object[])phases;
                            for (int i = 0; i < phasesArray.Length; i++)
                            {
                                List<object> list = Enumerable.Cast<object>((IEnumerable)phasesArray[i]).ToList();
                                FieldInfo initialSpawnNumberProp =
                                    AccessTools.Field(list[0].GetType(), "InitialSpawnNumber");
                                if (i == 0)
                                {
                                    initialSpawnNumberProp.SetValue(list[0],
                                        playerSide == BattleSideEnum.Defender
                                            ? initialPlayerSpawn
                                            : EnemyInitialSpawn);
                                    AccessTools.Field(list[0].GetType(), "RemainingSpawnNumber").SetValue(list[0],
                                        playerSide == BattleSideEnum.Defender
                                            ? PlayerArmySize - initialPlayerSpawn
                                            : EnemyArmySize - EnemyInitialSpawn);
                                }
                                else
                                {
                                    initialSpawnNumberProp.SetValue(list[0],
                                        playerSide == BattleSideEnum.Attacker
                                            ? initialPlayerSpawn
                                            : EnemyInitialSpawn);
                                    AccessTools.Field(list[0].GetType(), "RemainingSpawnNumber").SetValue(list[0],
                                        playerSide == BattleSideEnum.Attacker
                                            ? PlayerArmySize - initialPlayerSpawn
                                            : EnemyArmySize - EnemyInitialSpawn);
                                }

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        InformationManager.DisplayMessage(
                            new InformationMessage("ChooseYourTroops Error at MissionAgentSpawnLogic Postfix"));
                        setupSide = false;
                    }
                }
            }

        }
    }


    public class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            Harmony harmony = new Harmony("choose_your_troops");
            harmony.PatchAll();
        }
        public override void OnGameLoaded(Game game, object initializerObject)
        {
            base.OnCampaignStart(game, initializerObject);
            CampaignGameStarter gameStarter = (CampaignGameStarter)initializerObject;
            gameStarter.AddBehavior(new ChooseYourTroopsBehavior());
        }
    }
}