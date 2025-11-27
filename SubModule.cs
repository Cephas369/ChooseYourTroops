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
using System.Diagnostics;
using System.Linq;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Localization;
using System.Reflection;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers;
using SandBox.View.Menu;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.InputSystem;

namespace ChooseYourTroops
{
    public class ChooseYourTroopsBehavior : CampaignBehaviorBase
    {
        private static BattleSideEnum _playerSide = BattleSideEnum.None;
        private static TroopRoster? _actualTroopRoster = null;

        private static List<(FlattenedTroopRosterElement, MapEventParty, float)> _actualReadyTroopsPriorityList = null;

        private static bool _setupSide = false;
        private static bool _haveChosenTroops = false;
        private static int PlayerArmySize;
        private static int EnemyArmySize;
        private static int initialPlayerSpawn;

        public override void RegisterEvents()
        {
            CampaignEvents.MissionTickEvent.AddNonSerializedListener(this, (float dt) =>
            {
                if (Input.IsKeyReleased(InputKey.H))
                {
                    Mission mission = Mission.Current;
                    int trueAttackerAmount =
                        mission.AttackerTeam.ActiveAgents.Sum(agent => agent.Character.HasMount() ? 2 : 1);
                    
                    int trueDefenderAmount =
                        mission.DefenderTeam.ActiveAgents.Sum(agent => agent.Character.HasMount() ? 2 : 1);
                    
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject($"Attacker amount: {mission.AttackerTeam.ActiveAgents.Count} | true amount: {trueAttackerAmount}").ToString()));
                    
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject($"Defender amount: {mission.DefenderTeam.ActiveAgents.Count} | true amount: {trueDefenderAmount}").ToString()));
                    
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject($"All agents: {mission.AllAgents.Count}").ToString()));
                }
            });
            
            CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, (IMission mission) => CheckTroopRoster());
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, CheckTroopRoster);
            CampaignEvents.OnPartySizeChangedEvent.AddNonSerializedListener(this, (PartyBase party) =>
            {
                if (party == MobileParty.MainParty.Party && Mission.Current == null)
                    CheckTroopRoster();
            });
        }
        
        public static bool DoesTroopCountByTwo(CharacterObject characterObject) => ChooseYourTroopsConfig.Instance is { SupportForMaximumTroops: true } && characterObject.HasMount();

        private static void CheckTroopRoster()
        {
            if (_actualTroopRoster == null) 
                return;

            var partyRoster = MobileParty.MainParty. MemberRoster. GetTroopRoster();
            var actualRoster = _actualTroopRoster.GetTroopRoster();
    
            _actualTroopRoster. Clear();

            foreach (var actualTroop in actualRoster)
            {
                var partyTroop = partyRoster.FirstOrDefault(x => 
                    x.Character?. StringId == actualTroop. Character?.StringId);
        
                if (partyTroop. Character == null || 
                    partyTroop. Number <= 0 || 
                    partyTroop.Number == partyTroop.WoundedNumber)
                    continue;

                var healthyCount = partyTroop.Number - partyTroop.WoundedNumber;
                var finalCount = Math.Min(actualTroop.Number, healthyCount);
        
                var updatedTroop = actualTroop;
                updatedTroop. Number = finalCount;
                _actualTroopRoster.Add(updatedTroop);
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("actualTroopRoster", ref _actualTroopRoster);
        }
        
        public static class EncounterGameMenuBehaviorPatch
        {
            private static bool isPlayerArmyBig()
            {
                if (PlayerEncounter.Battle != null && PlayerEncounter.Battle.IsFinalized)
                    return false;
                if (MobileParty.MainParty.Army != null &&
                    MobileParty.MainParty.Army.LeaderParty.Name != MobileParty.MainParty.Name)
                    return false;
                if (PlayerEncounter.Battle != null && PlayerSiege.BesiegedSettlement == null &&
                    MobileParty.MainParty.Army == null &&
                    PlayerEncounter.Battle.PartiesOnSide(PlayerEncounter.Battle.PlayerSide).Count(x => !x.IsNpcParty) >
                    1 &&
                    PlayerEncounter.Battle.GetLeaderParty(PlayerEncounter.Battle.PlayerSide).Name !=
                    PartyBase.MainParty.Name)
                    return false;
                if (PlayerEncounter.Battle != null || MapEvent.PlayerMapEvent != null ||
                    PlayerEncounter.EncounteredParty?.MapEvent != null)
                {
                    MapEvent encounteredBattle = PlayerEncounter.Battle ??
                                                 MapEvent.PlayerMapEvent ?? PlayerEncounter.EncounteredParty?.MapEvent;
                    _playerSide = encounteredBattle.PlayerSide;
                    try
                    {
                        PlayerArmySize = encounteredBattle.GetMapEventSide(encounteredBattle.PlayerSide)
                            .GetTotalHealthyTroopCountOfSide();
                        EnemyArmySize = encounteredBattle
                            .GetMapEventSide(encounteredBattle.PlayerSide.GetOppositeSide())
                            .GetTotalHealthyTroopCountOfSide();
                    }
                    catch (Exception)
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


                    _playerSide = PlayerSiege.PlayerSide;
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
                            args.Tooltip =
                                new TextObject(
                                    "{=select_your_troops_tooltip}There is only one person or less on your party.");
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
                            args.Tooltip =
                                new TextObject(
                                    "{=select_your_troops_tooltip}There is only one person or less on your party.");
                        }

                        return condition;
                    }, new GameMenuOption.OnConsequenceDelegate(OpenTroopsSelector));
            }

            static bool CanChangeStatusOfTroop(CharacterObject character) => !character.IsPlayerCharacter;

            static void OnDone(TroopRoster troopRoster, MenuCallbackArgs args)
            {
                if (troopRoster.TotalManCount < initialPlayerSpawn)
                    initialPlayerSpawn = troopRoster.TotalManCount;
                if (!_haveChosenTroops)
                    InformationManager.DisplayMessage(new InformationMessage(
                        new TextObject("{=troops_selected_text}Troops selected!").ToString(), new Color(15, 70, 160)));
                _actualTroopRoster = troopRoster;
                _haveChosenTroops = true;
            }

            public static void OpenTroopSelection(MenuCallbackArgs args, TroopRoster fullRoster,
                TroopRoster initialSelections, Func<CharacterObject, bool> canChangeChangeStatusOfTroop,
                Action<TroopRoster> onDone, int maxSelectableTroopCount, int minSelectableTroopCount)
            {
                if (args.MenuContext.Handler != null)
                {
                    MenuViewContext menuView = args.MenuContext.Handler as MenuViewContext;
                    if (menuView != null)
                    {
                        AccessTools.Field(typeof(MenuViewContext), "_menuTroopSelection").SetValue(menuView,
                            menuView.AddMenuView<CYTGauntletMenuTroopSelectionView>(fullRoster, initialSelections,
                                canChangeChangeStatusOfTroop, onDone, maxSelectableTroopCount,
                                minSelectableTroopCount));
                    }
                }
            }

            private static void OpenTroopsSelector(MenuCallbackArgs args)
            {
                CheckTroopRoster();
                int initialPlayerTroops = PlayerArmySize;
                
                if (PlayerArmySize + EnemyArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit)
                {
                    if (PlayerArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2 ||
                        EnemyArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2)
                    {
                        if (EnemyArmySize < PlayerArmySize)
                        {
                            initialPlayerTroops = ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2 +
                                                  (ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2 - EnemyArmySize);
                        }

                        if (PlayerArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2 &&
                            EnemyArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2)
                            initialPlayerTroops = ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2;
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
                else if ((PlayerEncounter.Battle != null &&
                          PlayerEncounter.Battle.PartiesOnSide(_playerSide).Count > 1) ||
                         (PlayerSiege.BesiegedSettlement != null &&
                          PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(PlayerSiege.PlayerSide)
                              .GetInvolvedPartiesForEventType().Count() > 1) && MobileParty.MainParty.Army == null)
                {
                    armyRoster = TroopRoster.CreateDummyTroopRoster();
                    List<PartyBase> PartiesOnPlayerSide = PlayerEncounter.Battle != null
                        ? PlayerEncounter.Battle.PartiesOnSide(_playerSide).Select(x => x.Party).ToList()
                        : PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(PlayerSiege.PlayerSide)
                            .GetInvolvedPartiesForEventType().ToList();
                    foreach (PartyBase party in PartiesOnPlayerSide)
                    {
                        armyRoster.Add(party.MemberRoster);
                    }
                }

                initialPlayerSpawn = initialPlayerTroops;
                
                // First gets only the healthy and not hero troops
                TroopRoster dummyTroopRoster = TroopRoster.CreateDummyTroopRoster();
                foreach (var troop in armyRoster.GetTroopRoster().Where(x => x.Character.IsHero && x.Number > x.WoundedNumber))
                {
                    dummyTroopRoster.Add(troop);
                }
                    
                if (_actualTroopRoster != null)
                {
                    var soldiersRoster = _actualTroopRoster.CloneRosterData();
                    foreach (var soldier in soldiersRoster.GetTroopRoster())
                    {
                        if (soldier.WoundedNumber > 0)
                        {
                            soldiersRoster.AddToCounts(soldier.Character, -soldier.WoundedNumber);
                        }
                    }

                    if (soldiersRoster.TotalManCount + dummyTroopRoster.TotalManCount > initialPlayerTroops)
                    {
                        for (int i = initialPlayerTroops - 1 - dummyTroopRoster.TotalManCount; i < soldiersRoster.Count + dummyTroopRoster.TotalManCount - initialPlayerTroops; i++)
                        {
                            soldiersRoster.RemoveTroop(soldiersRoster.GetCharacterAtIndex(i), 1);
                        }
                    }
                    
                    foreach (var troop in soldiersRoster.GetTroopRoster().Where(x => !x.Character.IsHero))
                    {
                        dummyTroopRoster.AddToCounts(troop.Character, troop.Number - troop.WoundedNumber);
                    }
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
                        if (_setupSide && _actualTroopRoster != null && !__instance.MapEvent.IsPlayerSimulation && number == initialPlayerSpawn)
                        {
                            if (__instance.MissionSide == _playerSide && PlayerArmySize > 0)
                            {
                                allocatedTroops = new();
                                FlattenedTroopRoster flattenedTroopRoster = _actualTroopRoster.ToFlattenedRoster();
                                List<(FlattenedTroopRosterElement, MapEventParty, float)> list =
                                    (List<(FlattenedTroopRosterElement, MapEventParty, float)>)AccessTools
                                        .Field(typeof(MapEventSide), "_readyTroopsPriorityList").GetValue(__instance);
                                _actualReadyTroopsPriorityList = list.AsReadOnly().ToList();
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
                    if (_setupSide && __instance.MissionSide == _playerSide && number == initialPlayerSpawn &&
                        _actualTroopRoster != null)
                    {
                        try
                        {
                            _actualReadyTroopsPriorityList.RemoveAll(
                                x => allocatedTroops.ContainsKey(x.Item1.Descriptor));
                            
                            AccessTools.Field(typeof(MapEventSide), "_readyTroopsPriorityList")
                                .SetValue(__instance, _actualReadyTroopsPriorityList);
                            
                            AccessTools.Field(typeof(MapEventSide), "_allocatedTroops")
                                .SetValue(__instance, allocatedTroops);
                        }
                        catch (Exception e)
                        {
                            InformationManager.DisplayMessage(
                                new InformationMessage("ChooseYourTroops Error at AllocateTroops Postfix"));
                            _actualReadyTroopsPriorityList = null;
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
                    _playerSide = BattleSideEnum.None;
                    _setupSide = false;
                    _actualReadyTroopsPriorityList = null;
                    _haveChosenTroops = false;
                }
            }

            [HarmonyPatch(typeof(MissionAgentSpawnLogic), "AfterStart")]
            static class AfterStartPostfix
            {
                [HarmonyPostfix]
                static void Postfix(MissionAgentSpawnLogic __instance)
                {
                    if (_haveChosenTroops)
                    {
                        _setupSide = true;
                    }
                }
            }

            [HarmonyPatch(typeof(LordsHallFightMissionController), "OnCreated")]
            static class InitializeMissionPrefix
            {
                [HarmonyPrefix]
                static void Prefix()
                {
                    if (_setupSide)
                    {
                        _setupSide = false;
                        _haveChosenTroops = false;
                    }
                }
            }

            [HarmonyPatch(typeof(Mission), "OnBattleSideDeployed")]
            static class OnBattleSideDeployedPostfix
            {
                [HarmonyPostfix]
                static void Postfix(BattleSideEnum side)
                {
                    if (side == _playerSide)
                        PlayerArmySize = 0;
                    else
                        EnemyArmySize = 0;
                }
            }

            private static int EnemyInitialSpawn => EnemyArmySize <= ChooseYourTroopsConfig.CurrentBattleSizeLimit - initialPlayerSpawn
                ? EnemyArmySize
                : ChooseYourTroopsConfig.CurrentBattleSizeLimit - initialPlayerSpawn;
            
            /** Used to set the initial spawn of the player or the enemy taking into account if troops with mount should be counted as two */
            private static int GetEnemyInitialSpawn(IEnumerable<FlattenedTroopRosterElement> troops)
            {
                int playerInitialAgents = 0;
                foreach (var flattenedTroopRosterElement in _actualTroopRoster.ToFlattenedRoster())
                {
                    playerInitialAgents += DoesTroopCountByTwo(flattenedTroopRosterElement.Troop) ? 2 : 1;
                }
                
                int maxAgents = ChooseYourTroopsConfig.CurrentBattleSizeLimit - playerInitialAgents;

                int finalAmount = 0;
                int totalAgents = 0;

                for (int i = 0; i < troops.Count(); i++)
                {
                    int currentAgents = DoesTroopCountByTwo(troops.ElementAt(i).Troop) ? 2 : 1;
                    if (totalAgents + currentAgents > maxAgents)
                    {
                        break;
                    }

                    totalAgents += currentAgents;

                    finalAmount += 1;
                }

                return finalAmount;
            }
            
            private static readonly Type MissionSideType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.MissionAgentSpawnLogic+MissionSide");

            [HarmonyPatch(typeof(MissionAgentSpawnLogic), "Init")]
            static class MissionAgentSpawnLogicInitdPostfix
            {
                [HarmonyPostfix]
                static void Postfix(bool spawnDefenders, bool spawnAttackers,
                    in MissionSpawnSettings reinforcementSpawnSettings, MissionAgentSpawnLogic __instance)
                {
                    try
                    {
                        if (_haveChosenTroops)
                        {
                            var missionSidesField = AccessTools.Field(typeof(MissionAgentSpawnLogic), "_missionSides");
                            var missionSides = (Array)missionSidesField.GetValue(__instance);
                            
                            var phases = __instance.GetType()
                                .GetField("_phases", BindingFlags.NonPublic | BindingFlags.Instance)
                                .GetValue(__instance);
                            
                            object[] phasesArray = (object[])phases;
                            int enemyInitialSpawnCount = initialPlayerSpawn;
                            for (int i = 0; i < phasesArray.Length; i++)
                            {
                                List<object> list = Enumerable.Cast<object>((IEnumerable)phasesArray[i]).ToList();
                                
                                FieldInfo initialSpawnNumberProp =
                                    AccessTools.Field(list[0].GetType(), "InitialSpawnNumber");

                                bool isPlayerSide = i == 0 ? (_playerSide == BattleSideEnum.Defender) : (_playerSide == BattleSideEnum.Attacker);
                                
                                int currentInitialSpawn;

                                if (isPlayerSide)
                                {
                                    currentInitialSpawn = initialPlayerSpawn;
                                }
                                else
                                {
                                    if (ChooseYourTroopsConfig.Instance is { SupportForMaximumTroops: true })
                                    {
                                        var troopSupplier = (PartyGroupTroopSupplier)AccessTools
                                            .Field(MissionSideType, "_troopSupplier").GetValue(missionSides.GetValue(i));;
                                        var partyGroup = (MapEventSide)AccessTools.Property(typeof(PartyGroupTroopSupplier), "PartyGroup").GetValue(troopSupplier);
                                        var readyTroopsPriorityList = (List<ValueTuple<FlattenedTroopRosterElement, MapEventParty, float>>)AccessTools.Field(typeof(MapEventSide), "_readyTroopsPriorityList").GetValue(partyGroup);
                                        readyTroopsPriorityList =
                                            readyTroopsPriorityList.OrderByDescending(x => x.Item3).ToList();
                                        
                                        currentInitialSpawn = GetEnemyInitialSpawn(readyTroopsPriorityList.Select(item => item.Item1));
                                    }
                                    else
                                    {
                                        currentInitialSpawn = EnemyInitialSpawn;
                                    }

                                    enemyInitialSpawnCount = currentInitialSpawn;
                                }

                                initialSpawnNumberProp.SetValue(list[0], currentInitialSpawn);
                                
                                AccessTools.Field(list[0].GetType(), "RemainingSpawnNumber").SetValue(list[0],
                                    isPlayerSide
                                        ? PlayerArmySize - initialPlayerSpawn
                                        : EnemyArmySize - EnemyInitialSpawn);
                            }
                            
                            AccessTools.Method(Mission.Current.GetType(), "SetBattleAgentCount", null, null).Invoke(Mission.Current, new object[]
                            {
                                Math.Min(initialPlayerSpawn, enemyInitialSpawnCount)
                            });
                            
                            object missionSidesObj = missionSidesField.GetValue(__instance);
                            Array missionSidesArray = missionSidesObj as Array;
                            if (missionSidesArray != null)
                            {
                                object defenderSide = missionSidesArray.GetValue(0);
                                object attackerSide = missionSidesArray.GetValue(1);
                                MethodInfo setSpawnMethod = AccessTools.Method(defenderSide.GetType(), "SetSpawnTroops");
                                setSpawnMethod.Invoke(defenderSide, new object[] { spawnDefenders });
                                setSpawnMethod.Invoke(attackerSide, new object[] { spawnAttackers });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        InformationManager.DisplayMessage(
                            new InformationMessage("ChooseYourTroops Error at MissionAgentSpawnLogic Postfix"));
                        _setupSide = false;
                    }
                }
            }
        }
    }


    public class SubModule : MBSubModuleBase
    {
        private Harmony _harmony;
        private bool _gameMenuPatch = false;
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _harmony = new Harmony("choose_your_troops");
            _harmony.PatchAll();
        }

        public override void OnGameLoaded(Game game, object initializerObject)
        {
            base.OnCampaignStart(game, initializerObject);
            CampaignGameStarter gameStarter = (CampaignGameStarter)initializerObject;
            gameStarter.AddBehavior(new ChooseYourTroopsBehavior());

            if (!_gameMenuPatch)
            {
                var original = AccessTools.Method(typeof(EncounterGameMenuBehavior), "AddGameMenus");
                var postfix = AccessTools.Method(typeof(ChooseYourTroopsBehavior.EncounterGameMenuBehaviorPatch), "Postfix");
                _harmony.Patch(original, null, postfix);
                _gameMenuPatch = true;
            }
        }
    }
}