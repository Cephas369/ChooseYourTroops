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
using ChooseYourTroops;
using TaleWorlds.MountAndBlade.Source.Missions.Handlers;
using SandBox.View.Menu;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.InputSystem;

namespace ChooseYourTroops;

public class ChooseYourTroopsBehavior : CampaignBehaviorBase
{
    private static BattleSideEnum _playerSide = BattleSideEnum.None;
    private static TroopRoster? _actualTroopRoster = null;

    private static List<(FlattenedTroopRosterElement, MapEventParty, float)> _actualReadyTroopsPriorityList = null!;

    private static bool _setupSide = false;
    private static bool _haveChosenTroops = false;
    private static int _playerArmySize;
    private static int _enemyArmySize;
    private static int _initialPlayerSpawn;

    public override void RegisterEvents()
    {
        CampaignEvents.OnMissionEndedEvent.AddNonSerializedListener(this, (IMission mission) =>
        {
            CheckTroopRoster();
            EncounterGameMenuBehavior.AllocateTroopsPatch.Reset();
        });
        CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, CheckTroopRoster);
        CampaignEvents.OnAfterSessionLaunchedEvent.AddNonSerializedListener(this,
            EncounterGameMenuBehavior.AddGamesMenu);
        CampaignEvents.OnPartySizeChangedEvent.AddNonSerializedListener(this, (PartyBase party) =>
        {
            if (party == MobileParty.MainParty.Party && Mission.Current == null)
                CheckTroopRoster();
        });
    }

    internal static bool DoesTroopCountByTwo(CharacterObject characterObject)
    {
        return ChooseYourTroopsConfig.Instance is { SupportForMaximumTroops: true } && characterObject.HasMount();
    }

    internal static void DismissSelection()
    {
        EncounterGameMenuBehavior.AllocateTroopsPatch.Reset();
        _actualTroopRoster = null;
        _haveChosenTroops = false;
        
        Campaign.Current.CurrentMenuContext.Refresh();
    }

    private static void CheckTroopRoster()
    {
        if (_actualTroopRoster == null)
            return;

        var partyRoster = MobileParty.MainParty.MemberRoster.GetTroopRoster();
        var actualRoster = _actualTroopRoster.GetTroopRoster();

        _actualTroopRoster.Clear();

        foreach (var actualTroop in actualRoster)
        {
            var partyTroop = partyRoster.FirstOrDefault(x =>
                x.Character?.StringId == actualTroop.Character?.StringId);

            if (partyTroop.Character == null && actualTroop.Character?.HeroObject?.PartyBelongedTo?.Army == MobileParty.MainParty?.Army)
            {
                partyTroop = actualTroop;
            }

            if (partyTroop.Character == null ||
                partyTroop.Number <= 0 ||
                partyTroop.Number == partyTroop.WoundedNumber)
                continue;

            var healthyCount = partyTroop.Number - partyTroop.WoundedNumber;
            var finalCount = Math.Min(actualTroop.Number, healthyCount);

            var updatedTroop = actualTroop;
            updatedTroop.Number = finalCount;
            _actualTroopRoster.Add(updatedTroop);
        }
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("actualTroopRoster", ref _actualTroopRoster);
    }

    public static class EncounterGameMenuBehavior
    {
        private static bool IsPlayerArmyBig()
        {
            if (PlayerEncounter.Battle != null && PlayerEncounter.Battle.IsFinalized)
                return false;
            if (MobileParty.MainParty.Army != null &&
                MobileParty.MainParty.Army.LeaderParty.Name != MobileParty.MainParty.Name)
                return false;
            if (PlayerEncounter.Battle != null && PlayerSiege.BesiegedSettlement == null &&
                MobileParty.MainParty.Army == null &&
                PlayerEncounter.Battle.PartiesOnSide(PlayerEncounter.Battle.PlayerSide).Count(x => !x.IsNpcParty) > 1 &&
                PlayerEncounter.Battle.GetLeaderParty(PlayerEncounter.Battle.PlayerSide).Name != PartyBase.MainParty.Name)
                return false;

            if (PlayerEncounter.Battle != null || MapEvent.PlayerMapEvent != null ||
                PlayerEncounter.EncounteredParty?.MapEvent != null)
            {
                var encounteredBattle = PlayerEncounter.Battle ??
                                        MapEvent.PlayerMapEvent ?? PlayerEncounter.EncounteredParty?.MapEvent;
                _playerSide = encounteredBattle.PlayerSide;
                try
                {
                    _playerArmySize = encounteredBattle.GetMapEventSide(encounteredBattle.PlayerSide)
                        .GetTotalHealthyTroopCountOfSide();
                    _enemyArmySize = encounteredBattle
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
                _playerArmySize = PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(PlayerSiege.PlayerSide)
                    .GetInvolvedPartiesForEventType().Sum(x => x.NumberOfHealthyMembers);
                _enemyArmySize = PlayerSiege.PlayerSiegeEvent
                    .GetSiegeEventSide(PlayerSiege.PlayerSide.GetOppositeSide())
                    .GetInvolvedPartiesForEventType().Sum(x => x.NumberOfHealthyMembers);
                _playerSide = PlayerSiege.PlayerSide;
            }
            else
            {
                InformationManager.DisplayMessage(
                    new InformationMessage("ChooseYourTroops Error: Enemy party or besieged settlement not found."));
                return false;
            }

            return true;
        }

        public static void AddGamesMenu(CampaignGameStarter gameSystemInitializer)
        {
            gameSystemInitializer.AddGameMenuOption("encounter", "select_troops",
                "{=select_your_troops_text}Select troops ({SELECTED_AMOUNT} selected).", delegate(MenuCallbackArgs args)
                {
                    GameTexts.SetVariable("SELECTED_AMOUNT", _setupSide != null && _actualTroopRoster != null ? _actualTroopRoster.TotalManCount : 0);
                    args.optionLeaveType = GameMenuOption.LeaveType.TroopSelection;
                    args.IsEnabled = true;
                    var condition = IsPlayerArmyBig();
                    if (_playerArmySize <= 1)
                    {
                        args.IsEnabled = false;
                        args.Tooltip = new TextObject("{=select_your_troops_tooltip}There is only one person or less on your party.");
                    }
                    return condition;
                }, new GameMenuOption.OnConsequenceDelegate(OpenTroopsSelector));

            gameSystemInitializer.AddGameMenuOption("naval_storyline_encounter", "select_troops",
                "{=select_your_troops_text}Select your troops.".ToString(), delegate(MenuCallbackArgs args)
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.TroopSelection;
                    args.IsEnabled = true;
                    var condition = IsPlayerArmyBig();
                    if (_playerArmySize <= 1)
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
                    var condition = IsPlayerArmyBig();
                    if (_playerArmySize <= 1)
                    {
                        args.IsEnabled = false;
                        args.Tooltip = new TextObject("{=select_your_troops_tooltip}There is only one person or less on your party.");
                    }
                    return condition;
                }, new GameMenuOption.OnConsequenceDelegate(OpenTroopsSelector));
        }

        private static bool CanChangeStatusOfTroop(CharacterObject character)
        {
            return !character.IsPlayerCharacter;
        }

        private static void OnDone(TroopRoster troopRoster, MenuCallbackArgs args)
        {
            if (troopRoster.TotalManCount < _initialPlayerSpawn)
                _initialPlayerSpawn = troopRoster.TotalManCount;
            if (!_haveChosenTroops || _haveChosenTroops && !troopRoster.Equals(_actualTroopRoster))
                InformationManager.DisplayMessage(new InformationMessage(
                    new TextObject("{=troops_selected_text}Troops selected!").ToString(), new Color(15, 70, 160)));
            _actualTroopRoster = troopRoster;
            _haveChosenTroops = true;
            
            GameTexts.SetVariable("SELECTED_AMOUNT", _actualTroopRoster?.TotalManCount ?? 0);
            
            args.MenuContext.Refresh();
        }

        public static void OpenTroopSelection(MenuCallbackArgs args, TroopRoster fullRoster,
            TroopRoster initialSelections, Func<CharacterObject, bool> canChangeChangeStatusOfTroop,
            Action<TroopRoster> onDone, int maxSelectableTroopCount, int minSelectableTroopCount)
        {
            if (args.MenuContext.Handler != null)
            {
                var menuView = args.MenuContext.Handler as MenuViewContext;
                if (menuView != null)
                    AccessTools.Field(typeof(MenuViewContext), "_menuTroopSelection").SetValue(menuView,
                        menuView.AddMenuView<CYTGauntletMenuTroopSelectionView>(fullRoster, initialSelections,
                            canChangeChangeStatusOfTroop, onDone, maxSelectableTroopCount, minSelectableTroopCount));
            }
        }

        private static void OpenTroopsSelector(MenuCallbackArgs args)
        {
            CheckTroopRoster();
            var initialPlayerTroops = _playerArmySize;

            if (_playerArmySize + _enemyArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit)
                if (_playerArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2 ||
                    _enemyArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2)
                {
                    if (_enemyArmySize < _playerArmySize)
                        initialPlayerTroops = ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2 +
                                              (ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2 - _enemyArmySize);

                    if (_playerArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2 &&
                        _enemyArmySize > ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2)
                        initialPlayerTroops = ChooseYourTroopsConfig.CurrentBattleSizeLimit / 2;
                }

            var armyRoster = MobileParty.MainParty.MemberRoster;

            if (MobileParty.MainParty.Army != null &&
                MobileParty.MainParty.Army.LeaderParty.Name == MobileParty.MainParty.Name)
            {
                armyRoster = TroopRoster.CreateDummyTroopRoster();
                foreach (var party in MobileParty.MainParty.Army.Parties) armyRoster.Add(party.MemberRoster);
            }
            else if ((PlayerEncounter.Battle != null &&
                      PlayerEncounter.Battle.PartiesOnSide(_playerSide).Count > 1) ||
                     (PlayerSiege.BesiegedSettlement != null &&
                      PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(PlayerSiege.PlayerSide)
                          .GetInvolvedPartiesForEventType().Count() > 1 && MobileParty.MainParty.Army == null))
            {
                armyRoster = TroopRoster.CreateDummyTroopRoster();
                var PartiesOnPlayerSide = PlayerEncounter.Battle != null
                    ? PlayerEncounter.Battle.PartiesOnSide(_playerSide).Select(x => x.Party).ToList()
                    : PlayerSiege.PlayerSiegeEvent.GetSiegeEventSide(PlayerSiege.PlayerSide)
                        .GetInvolvedPartiesForEventType().ToList();
                foreach (var party in PartiesOnPlayerSide) armyRoster.Add(party.MemberRoster);
            }

            _initialPlayerSpawn = initialPlayerTroops;

            // First gets only the healthy and not hero troops
            var dummyTroopRoster = TroopRoster.CreateDummyTroopRoster();

            if (_actualTroopRoster != null)
            {
                var soldiersRoster = _actualTroopRoster.CloneRosterData();
                foreach (var soldier in soldiersRoster.GetTroopRoster())
                    if (soldier.WoundedNumber > 0)
                        soldiersRoster.AddToCounts(soldier.Character, -soldier.WoundedNumber);

                if (soldiersRoster.TotalManCount > initialPlayerTroops)
                {
                    for (var i = initialPlayerTroops - 1 - dummyTroopRoster.TotalManCount;
                         i < soldiersRoster.Count - initialPlayerTroops;
                         i++)
                    {
                        soldiersRoster.RemoveTroop(soldiersRoster.GetCharacterAtIndex(i), 1);
                    }
                }

                foreach (var troop in soldiersRoster.GetTroopRoster())
                    dummyTroopRoster.AddToCounts(troop.Character, troop.Number - troop.WoundedNumber);
            }
            else
            {
                foreach (var troop in armyRoster.GetTroopRoster()
                             .Where(x => x.Character.IsHero && x.Number > x.WoundedNumber))
                {
                    dummyTroopRoster.Add(troop);
                }

                var strongestAndPriorTroops =
                    MobilePartyHelper.GetStrongestAndPriorTroops(MobileParty.MainParty,
                        initialPlayerTroops - dummyTroopRoster.TotalManCount, false);
                dummyTroopRoster.Add(strongestAndPriorTroops);
            }

            OpenTroopSelection(args, armyRoster, dummyTroopRoster,
                new Func<CharacterObject, bool>(CanChangeStatusOfTroop),
                delegate(TroopRoster roster) { OnDone(roster, args); }, initialPlayerTroops, 1);
        }

        [HarmonyPatch(typeof(MapEventSide), "AllocateTroops")]
        public static class AllocateTroopsPatch
        {
            private static HashSet<string> _selectedTroops;

            public static void Reset()
            {
                _selectedTroops = null;
            }

            [HarmonyPrefix]
            static void Prefix(
                ref List<UniqueTroopDescriptor> troopsList,
                int numberToAllocate,
                ref Func<UniqueTroopDescriptor, MapEventParty, bool> customAllocationConditions,
                MapEventSide __instance,
                List<(FlattenedTroopRosterElement, MapEventParty, float)> ____readyTroopsPriorityList
                )
            {
                try
                {
                    if (!_setupSide) return;
                    if (_actualTroopRoster == null) return;
                    if (__instance.MapEvent.IsPlayerSimulation) return;
                    if (__instance.MissionSide != _playerSide) return;

                    if (numberToAllocate != _initialPlayerSpawn)
                    {
                        customAllocationConditions = null;
                        return;
                    }

                    if (_selectedTroops == null)
                    {
                        _selectedTroops = new HashSet<string>();
                        foreach (var troop in _actualTroopRoster.GetTroopRoster())
                        {
                            if (troop.Character == null) continue;
                            int healthy = troop.Number - troop.WoundedNumber;
                            if (healthy <= 0) continue;
                            _selectedTroops.Add(troop.Character.StringId);
                        }
                    }

                    customAllocationConditions = (descriptor, party) =>
                    {
                        try
                        {
                            CharacterObject troop = party.Troops[descriptor].Troop;
                            if (troop == null) return false;
                            return _selectedTroops.Contains(troop.StringId);
                        }
                        catch
                        {
                            return false;
                        }
                    };
                }
                catch (Exception ex)
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage($"CYT AllocateTroops Error:\n{ex}"));
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerEncounter), "FinishEncounterInternal")]
    private static class PlayerEncounterPrefix
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            EncounterGameMenuBehavior.AllocateTroopsPatch.Reset();
            _actualTroopRoster = null;
            _playerSide = BattleSideEnum.None;
            _setupSide = false;
            _haveChosenTroops = false;
            _playerArmySize = 0;
            _enemyArmySize = 0;
            _initialPlayerSpawn = 0;
        }
    }

    // ── MUDANÇA: MissionAgentSpawnLogic → DefaultBattleMissionAgentSpawnLogic ──
    [HarmonyPatch(typeof(DefaultBattleMissionAgentSpawnLogic), "AfterStart")]
    private static class AfterStartPostfix
    {
        [HarmonyPostfix]
        private static void Postfix(DefaultBattleMissionAgentSpawnLogic __instance)
        {
            if (_haveChosenTroops) _setupSide = true;
        }
    }

    [HarmonyPatch(typeof(LordsHallFightMissionController), "OnCreated")]
    private static class InitializeMissionPrefix
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (_setupSide)
            {
                _setupSide = false;
                _haveChosenTroops = false;
            }
        }
    }

    [HarmonyPatch(typeof(Mission), "OnBattleSideDeployed")]
    private static class OnBattleSideDeployedPostfix
    {
        [HarmonyPostfix]
        private static void Postfix(BattleSideEnum side)
        {
            if (side == _playerSide)
                _playerArmySize = 0;
            else
                _enemyArmySize = 0;
        }
    }

    private static int EnemyInitialSpawn =>
        _enemyArmySize <= ChooseYourTroopsConfig.CurrentBattleSizeLimit - _initialPlayerSpawn
            ? _enemyArmySize
            : ChooseYourTroopsConfig.CurrentBattleSizeLimit - _initialPlayerSpawn;
    
    /** Used to set the initial spawn of the player or the enemy taking into account if troops with mount should be counted as two */
    private static int GetEnemyInitialSpawn(IEnumerable<FlattenedTroopRosterElement> troops)
    {
        var playerInitialAgents = 0;
        foreach (var flattenedTroopRosterElement in _actualTroopRoster.ToFlattenedRoster())
            playerInitialAgents += DoesTroopCountByTwo(flattenedTroopRosterElement.Troop) ? 2 : 1;

        var maxAgents = ChooseYourTroopsConfig.CurrentBattleSizeLimit - playerInitialAgents;
        var finalAmount = 0;
        var totalAgents = 0;

        for (var i = 0; i < troops.Count(); i++)
        {
            var currentAgents = DoesTroopCountByTwo(troops.ElementAt(i).Troop) ? 2 : 1;
            if (totalAgents + currentAgents > maxAgents) break;
            totalAgents += currentAgents;
            finalAmount += 1;
        }

        return finalAmount;
    }

    // ── MUDANÇA: substituído MissionSideType (inner class MissionSide)
    //            por MissionBattleSideSpawnContext (classe pública separada) ──
    private static readonly Type MissionBattleSideSpawnContextType =
        AccessTools.TypeByName("TaleWorlds.MountAndBlade.MissionBattleSideSpawnContext");

    // ── MUDANÇA: alvo do patch agora é DefaultBattleMissionAgentSpawnLogic.Init (privado) ──
    [HarmonyPatch(typeof(DefaultBattleMissionAgentSpawnLogic), "Init")]
    private static class DefaultBattleMissionAgentSpawnLogicInitPostfix
    {
        [HarmonyPostfix]
        private static void Postfix(
            bool spawnDefenders,
            bool spawnAttackers,
            in MissionSpawnSettings reinforcementSpawnSettings,
            DefaultBattleMissionAgentSpawnLogic __instance)
        {
            try
            {
                if (!_haveChosenTroops) return;

                // ── MUDANÇA: _missionSides → _battleSideSpawnContexts ──
                var battleSideSpawnContextsField = AccessTools.Field(
                    typeof(DefaultBattleMissionAgentSpawnLogic), "_battleSideSpawnContexts");
                var battleSideSpawnContexts = battleSideSpawnContextsField.GetValue(__instance) as Array;

                var phasesField = AccessTools.Field(
                    typeof(DefaultBattleMissionAgentSpawnLogic), "_phases");
                var phasesArray = (object[])phasesField.GetValue(__instance);

                var enemyInitialSpawnCount = _initialPlayerSpawn;

                for (var i = 0; i < phasesArray.Length; i++)
                {
                    var list = Enumerable.Cast<object>((IEnumerable)phasesArray[i]).ToList();
                    var initialSpawnNumberProp = AccessTools.Field(list[0].GetType(), "InitialSpawnNumber");

                    var isPlayerSide = i == 0
                        ? _playerSide == BattleSideEnum.Defender
                        : _playerSide == BattleSideEnum.Attacker;

                    int currentInitialSpawn;

                    if (isPlayerSide)
                    {
                        currentInitialSpawn = _initialPlayerSpawn;
                    }
                    else
                    {
                        if (ChooseYourTroopsConfig.Instance is { SupportForMaximumTroops: true })
                        {
                            // ── MUDANÇA: acessa _troopSupplier via MissionBattleSideSpawnContext ──
                            var sideSpawnContext = battleSideSpawnContexts!.GetValue(i);
                            
                            var troopSupplier = (PartyGroupTroopSupplier)AccessTools
                                .Field(MissionBattleSideSpawnContextType, "_troopSupplier")
                                .GetValue(sideSpawnContext);
                            
                            var partyGroup = (MapEventSide)AccessTools
                                .Property(typeof(PartyGroupTroopSupplier), "PartyGroup")
                                .GetValue(troopSupplier);
                            
                            var readyTroopsPriorityList =
                                (List<ValueTuple<FlattenedTroopRosterElement, MapEventParty, float>>)AccessTools
                                    .Field(typeof(MapEventSide), "_readyTroopsPriorityList")
                                    .GetValue(partyGroup);
                            
                            readyTroopsPriorityList =
                                readyTroopsPriorityList.OrderByDescending(x => x.Item3).ToList();
                            
                            currentInitialSpawn =
                                GetEnemyInitialSpawn(readyTroopsPriorityList.Select(item => item.Item1));
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
                            ? _playerArmySize - _initialPlayerSpawn
                            : _enemyArmySize - EnemyInitialSpawn);
                }

                // ── MUDANÇA: re-aplica SetSpawnTroops via _battleSideSpawnContexts ──
                if (battleSideSpawnContexts != null)
                {
                    var defenderSide = battleSideSpawnContexts.GetValue(0);
                    var attackerSide = battleSideSpawnContexts.GetValue(1);
                    var setSpawnMethod = AccessTools.Method(defenderSide!.GetType(), "SetSpawnTroops");
                    setSpawnMethod.Invoke(defenderSide, new object[] { spawnDefenders });
                    setSpawnMethod.Invoke(attackerSide, new object[] { spawnAttackers });
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage("ChooseYourTroops Error at DefaultBattleMissionAgentSpawnLogic Init Postfix"));
                _setupSide = false;
            }
        }
    }
}

public class SubModule : MBSubModuleBase
{
    private Harmony _harmony;

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        _harmony = new Harmony("choose_your_troops");
        _harmony.PatchAll();
    }

    public override void OnGameLoaded(Game game, object initializerObject)
    {
        base.OnCampaignStart(game, initializerObject);
        var gameStarter = (CampaignGameStarter)initializerObject;
        gameStarter.AddBehavior(new ChooseYourTroopsBehavior());
    }
}