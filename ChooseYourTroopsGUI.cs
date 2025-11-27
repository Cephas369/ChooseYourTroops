using System;
using System.Collections.Generic;
using System.Linq;
using SandBox.GauntletUI.Menu;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.TroopSelection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;
using SandBox.ViewModelCollection.Input;
using SandBox.View.Map;
using SandBox.View.Menu;
using TaleWorlds.GauntletUI.Data;
using TaleWorlds.TwoDimension;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.Core.ViewModelCollection.Selector;

namespace ChooseYourTroops
{
    public class CYTGameMenuTroopSelectionVM : ViewModel
    {

        private Dictionary<TroopSelectionItemVM, int> _maxAmounts = new Dictionary<TroopSelectionItemVM, int>();
        private List<TroopSelectionItemVM> _troopsCountedByTwo = new();
        public CYTGameMenuTroopSelectionVM(TroopRoster fullRoster, TroopRoster initialSelections, Func<CharacterObject, bool> canChangeChangeStatusOfTroop, Action<TroopRoster> onDone, int maxSelectableTroopCount, int minSelectableTroopCount)
        {
            _canChangeChangeStatusOfTroop = canChangeChangeStatusOfTroop;
            _onDone = onDone;
            _fullRoster = fullRoster;
            _initialSelections = initialSelections;
            _maxSelectableTroopCount = maxSelectableTroopCount;
            _minSelectableTroopCount = minSelectableTroopCount;
            InitList();
            RefreshValues();
            OnCurrentSelectedAmountChange();

            var spriteData = UIResourceManager.SpriteData;
            _category = spriteData.SpriteCategories["ui_partyscreen"];
            _category.Load(UIResourceManager.ResourceContext, UIResourceManager.ResourceDepot);

        }
        private SpriteCategory _category;
        // Token: 0x06000DF8 RID: 3576 RVA: 0x000389EC File Offset: 0x00036BEC
        public override void RefreshValues()
        {
            base.RefreshValues();
            TitleText = _titleTextObject.ToString();
            CurrentSelectedAmountTitle = _chosenTitleTextObject.ToString();
            DoneText = GameTexts.FindText("str_done", null).ToString();
            CancelText = GameTexts.FindText("str_cancel", null).ToString();
            ClearSelectionText = new TextObject("{=QMNWbmao}Clear Selection", null).ToString();

            OrderByTierText = new TextObject("{=cyt_order_by_tier}Order by tier", null).ToString();
            OrderByNameText = new TextObject("{=cyt_order_by_name}Order by name", null).ToString();
            OrderByCountText = new TextObject("{=cyt_order_by_count}Order by count", null).ToString();
            OrderByClassText = new TextObject("{=cyt_order_by_class}Order by class", null).ToString();
            OrderByCultureText = new TextObject("{=cyt_order_by_culture}Order by culture", null).ToString();

            InfantryAmount = _infantryAmount;
            ArcherAmount = _archerAmount;
            CavalryAmount = _cavalryAmount;
            HorseArcherAmount = _horseArcherAmount;
        }

        private void InitList()
        {
            Troops = new MBBindingList<TroopSelectionItemVM>();
            _currentTotalSelectedTroopCount = 0;
            foreach (TroopRosterElement troopRosterElement in _fullRoster.GetTroopRoster())
            {
                
                TroopSelectionItemVM troopSelectionItemVM = new TroopSelectionItemVM(troopRosterElement, new Action<TroopSelectionItemVM>(OnAddCount), new Action<TroopSelectionItemVM>(OnRemoveCount));
                troopSelectionItemVM.IsLocked = (!_canChangeChangeStatusOfTroop(troopRosterElement.Character) || troopRosterElement.Number - troopRosterElement.WoundedNumber <= 0);

                if (ChooseYourTroopsBehavior.DoesTroopCountByTwo(troopSelectionItemVM.Troop.Character))
                {
                    _troopsCountedByTwo.Add(troopSelectionItemVM);
                    troopSelectionItemVM.Name += " (+2)";
                }
                
                Troops.Add(troopSelectionItemVM);
                
                int troopCount = _initialSelections.GetTroopCount(troopRosterElement.Character);
                if (troopCount > 0)
                {
                    troopSelectionItemVM.CurrentAmount = troopCount;
                    
                    ChangeTotalSelectedAccordingToTroop(troopCount, troopSelectionItemVM);

                    for(int i = 0; i < troopCount; i++)
                        ChangeTroopValue(troopSelectionItemVM.Troop.Character.DefaultFormationClass, true);
                }
            }
            Troops.Sort(new CYTTroopItemComparer("class", false));
            
            _maxAmounts = _troopsCountedByTwo.ToDictionary(troop => troop, troop => int.Parse(troop.MaxAmount.ToString()));
            //Troops = (MBBindingList<TroopSelectionItemVM>)Troops.OrderBy(x=>x.Troop.Character.Name);
        }

        /** If configuration allows max battle size by 2000, troops with mounts should be counted as 2 */
        private void ChangeTotalSelectedAccordingToTroop(int amount, TroopSelectionItemVM troopItem)
        {
            bool isEngineMaxTroopsEnabled = ChooseYourTroopsConfig.Instance is { SupportForMaximumTroops: true };
            if (isEngineMaxTroopsEnabled)
            {
                if (ChooseYourTroopsBehavior.DoesTroopCountByTwo(troopItem.Troop.Character))
                {
                    _currentTotalSelectedTroopCount += amount * 2;
                    
                }
                else
                {
                    _currentTotalSelectedTroopCount += amount;
                }
                
                if (_currentTotalSelectedTroopCount > _maxSelectableTroopCount)
                {
                    IsEntireStackModifierActive = false;
                    IsFiveStackModifierActive = false;
                    OnRemoveCount(troopItem);
                }
            }
            else
            {
                _currentTotalSelectedTroopCount += amount;
            }

            if (isEngineMaxTroopsEnabled)
            {
                foreach (var troopSelectionItemVm in _troopsCountedByTwo)
                {
                    if (_maxAmounts.TryGetValue(troopSelectionItemVm, out int initialMaxTroopAmount))
                    {
                        troopSelectionItemVm.MaxAmount = _currentTotalSelectedTroopCount + 2 > _maxSelectableTroopCount
                        ? (troopSelectionItemVm.CurrentAmount == 0 ? 2 : troopSelectionItemVm.CurrentAmount)
                        : initialMaxTroopAmount;
                    }
                }
            }
        }
        
        private void OnRemoveCount(TroopSelectionItemVM troopItem)
        {
            if (troopItem.CurrentAmount > 0)
            {
                int num = 1;
                if (IsEntireStackModifierActive)
                {
                    num = troopItem.CurrentAmount;
                }
                else if (IsFiveStackModifierActive)
                {
                    num = MathF.Min(troopItem.CurrentAmount, 5);
                }
                
                troopItem.CurrentAmount -= num;
                ChangeTotalSelectedAccordingToTroop(-num, troopItem);
                for (int i = 0; i < num; i++)
                    ChangeTroopValue(troopItem.Troop.Character.DefaultFormationClass, false);
            }
            OnCurrentSelectedAmountChange();
        }

        private void OnAddCount(TroopSelectionItemVM troopItem)
        {
            if (troopItem.CurrentAmount < troopItem.MaxAmount && _currentTotalSelectedTroopCount < _maxSelectableTroopCount)
            {
                int num = 1;
                
                if (IsEntireStackModifierActive)
                {
                    num = MathF.Min(troopItem.MaxAmount - troopItem.CurrentAmount, _maxSelectableTroopCount - _currentTotalSelectedTroopCount);
                }
                else if (IsFiveStackModifierActive)
                {
                    num = MathF.Min(MathF.Min(troopItem.MaxAmount - troopItem.CurrentAmount, _maxSelectableTroopCount - _currentTotalSelectedTroopCount), 5);
                }
                troopItem.CurrentAmount += num;
                
                ChangeTotalSelectedAccordingToTroop(num, troopItem);
                for(int i = 0; i < num; i++)
                    ChangeTroopValue(troopItem.Troop.Character.DefaultFormationClass, true);
            }
            OnCurrentSelectedAmountChange();
        }

        private void OnCurrentSelectedAmountChange()
        {
            foreach (TroopSelectionItemVM troopSelectionItemVM in Troops)
            {
                troopSelectionItemVM.IsRosterFull = (_currentTotalSelectedTroopCount >= _maxSelectableTroopCount);
            }
            GameTexts.SetVariable("LEFT", _currentTotalSelectedTroopCount);
            GameTexts.SetVariable("RIGHT", _maxSelectableTroopCount);
            CurrentSelectedAmountText = GameTexts.FindText("str_LEFT_over_RIGHT_in_paranthesis", null).ToString();
            IsDoneEnabled = (_currentTotalSelectedTroopCount <= _maxSelectableTroopCount && _currentTotalSelectedTroopCount >= _minSelectableTroopCount);
        }

        private void OnDone()
        {
            TroopRoster troopRoster = TroopRoster.CreateDummyTroopRoster();
            foreach (TroopSelectionItemVM troopSelectionItemVM in Troops)
            {
                if (troopSelectionItemVM.CurrentAmount > 0)
                {
                    troopRoster.AddToCounts(troopSelectionItemVM.Troop.Character, troopSelectionItemVM.CurrentAmount, false, 0, 0, true, -1);
                }
            }
            IsEnabled = false;
            Common.DynamicInvokeWithLog(_onDone, new object[]
            {
                troopRoster
            });
        }

        public void ExecuteDone()
        {
            if (_currentTotalSelectedTroopCount < _maxSelectableTroopCount)
            {
                string text = new TextObject("{=z2Slmx4N}There are still some room for more soldiers. Do you want to proceed?", null).ToString();
                InformationManager.ShowInquiry(new InquiryData(TitleText, text, true, true, GameTexts.FindText("str_yes", null).ToString(), GameTexts.FindText("str_no", null).ToString(), new Action(OnDone), null, "", 0f, null, null, null), false, false);
                return;
            }
            OnDone();
        }

        public void ExecuteCancel()
        {
            IsEnabled = false;
        }

        public void ExecuteReset()
        {
            InitList();
            OnCurrentSelectedAmountChange();
        }

        public void ExecuteClearSelection()
        {
            Troops.ApplyActionOnAllItems(delegate (TroopSelectionItemVM troopItem)
            {
                if (_canChangeChangeStatusOfTroop(troopItem.Troop.Character))
                {
                    int currentAmount = troopItem.CurrentAmount;
                    for (int i = 0; i < currentAmount; i++)
                    {
                        troopItem.ExecuteRemove();
                    }
                }
            });
        }

        public void OrderByTier()
        {
            Troops.Sort(new CYTTroopItemComparer("tier", IsAscending));
            RefreshValues();
        }
        public void OrderByClass()
        {
            Troops.Sort(new CYTTroopItemComparer("class", IsAscending));
            RefreshValues();
        }
        public void OrderByName()
        {
            Troops.Sort(new CYTTroopItemComparer("name", IsAscending));
            RefreshValues();
        }
        public void OrderByCount()
        {
            Troops.Sort(new CYTTroopItemComparer("count", IsAscending));
            RefreshValues();
        }
        public void OrderByCulture()
        {
            Troops.Sort(new CYTTroopItemComparer("culture", IsAscending));
            RefreshValues();
        }

        // Token: 0x06000E02 RID: 3586 RVA: 0x00038E6A File Offset: 0x0003706A
        public override void OnFinalize()
        {
            base.OnFinalize();
            InputKeyItemVM cancelInputKey = CancelInputKey;
            if (cancelInputKey != null)
            {
                cancelInputKey.OnFinalize();
            }
            InputKeyItemVM doneInputKey = DoneInputKey;
            if (doneInputKey != null)
            {
                doneInputKey.OnFinalize();
            }
            InputKeyItemVM resetInputKey = ResetInputKey;
            if (resetInputKey == null)
            {
                return;
            }
            resetInputKey.OnFinalize();
            _category.Unload();
        }

        public void ChangeTroopValue(FormationClass formationClass, bool isSum)
        {
            switch (formationClass)
            {
                case FormationClass.Infantry:
                    if(isSum)
                        InfantryAmount += 1;
                    else
                        InfantryAmount -= 1;
                    break;
                case FormationClass.Ranged:
                    if (isSum)
                        ArcherAmount += 1;
                    else
                        ArcherAmount -= 1;
                    break;
                case FormationClass.Cavalry:
                    if (isSum)
                        CavalryAmount += 1;
                    else
                        CavalryAmount -= 1;
                    break;
                case FormationClass.HorseArcher:
                    if (isSum)
                        HorseArcherAmount += 1;
                    else
                        HorseArcherAmount -= 1;
                    break;
            }
        }
        private void ExecuteToggleOrder()
        {
            IsAscending = !IsAscending;
        }
        // Token: 0x06000E03 RID: 3587 RVA: 0x00038EA4 File Offset: 0x000370A4
        public void SetCancelInputKey(HotKey hotkey)
        {
            CancelInputKey = InputKeyItemVM.CreateFromHotKey(hotkey, true);
        }

        // Token: 0x06000E04 RID: 3588 RVA: 0x00038EB3 File Offset: 0x000370B3
        public void SetDoneInputKey(HotKey hotkey)
        {
            DoneInputKey = InputKeyItemVM.CreateFromHotKey(hotkey, true);
        }

        // Token: 0x06000E05 RID: 3589 RVA: 0x00038EC2 File Offset: 0x000370C2
        public void SetResetInputKey(HotKey hotkey)
        {
            ResetInputKey = InputKeyItemVM.CreateFromHotKey(hotkey, true);
        }

        // Token: 0x1700048F RID: 1167
        // (get) Token: 0x06000E06 RID: 3590 RVA: 0x00038ED1 File Offset: 0x000370D1
        // (set) Token: 0x06000E07 RID: 3591 RVA: 0x00038ED9 File Offset: 0x000370D9
        [DataSourceProperty]
        public InputKeyItemVM DoneInputKey
        {
            get
            {
                return _doneInputKey;
            }
            set
            {
                if (value != _doneInputKey)
                {
                    _doneInputKey = value;
                    base.OnPropertyChangedWithValue<InputKeyItemVM>(value, "DoneInputKey");
                }
            }
        }
        [DataSourceProperty]
        public bool IsAscending
        {
            get
            {
                return _isAscending;
            }
            set
            {
                if (value != _isAscending)
                {
                    _isAscending = value;
                    base.OnPropertyChangedWithValue(value, "IsAscending");
                }
            }
        }
        private bool _isAscending;
        // Token: 0x17000490 RID: 1168
        // (get) Token: 0x06000E08 RID: 3592 RVA: 0x00038EF7 File Offset: 0x000370F7
        // (set) Token: 0x06000E09 RID: 3593 RVA: 0x00038EFF File Offset: 0x000370FF
        [DataSourceProperty]
        public InputKeyItemVM CancelInputKey
        {
            get
            {
                return _cancelInputKey;
            }
            set
            {
                if (value != _cancelInputKey)
                {
                    _cancelInputKey = value;
                    base.OnPropertyChangedWithValue<InputKeyItemVM>(value, "CancelInputKey");
                }
            }
        }

        // Token: 0x17000491 RID: 1169
        // (get) Token: 0x06000E0A RID: 3594 RVA: 0x00038F1D File Offset: 0x0003711D
        // (set) Token: 0x06000E0B RID: 3595 RVA: 0x00038F25 File Offset: 0x00037125
        [DataSourceProperty]
        public InputKeyItemVM ResetInputKey
        {
            get
            {
                return _resetInputKey;
            }
            set
            {
                if (value != _resetInputKey)
                {
                    _resetInputKey = value;
                    base.OnPropertyChangedWithValue<InputKeyItemVM>(value, "ResetInputKey");
                }
            }
        }

        // Token: 0x17000492 RID: 1170
        // (get) Token: 0x06000E0C RID: 3596 RVA: 0x00038F43 File Offset: 0x00037143
        // (set) Token: 0x06000E0D RID: 3597 RVA: 0x00038F4B File Offset: 0x0003714B
        [DataSourceProperty]
        public bool IsEnabled
        {
            get
            {
                return _isEnabled;
            }
            set
            {
                if (value != _isEnabled)
                {
                    _isEnabled = value;
                    base.OnPropertyChangedWithValue(value, "IsEnabled");
                }
            }
        }

        // Token: 0x17000493 RID: 1171
        // (get) Token: 0x06000E0E RID: 3598 RVA: 0x00038F69 File Offset: 0x00037169
        // (set) Token: 0x06000E0F RID: 3599 RVA: 0x00038F71 File Offset: 0x00037171
        [DataSourceProperty]
        public bool IsDoneEnabled
        {
            get
            {
                return _isDoneEnabled;
            }
            set
            {
                if (value != _isDoneEnabled)
                {
                    _isDoneEnabled = value;
                    base.OnPropertyChangedWithValue(value, "IsDoneEnabled");
                }
            }
        }

        // Token: 0x17000494 RID: 1172
        // (get) Token: 0x06000E10 RID: 3600 RVA: 0x00038F8F File Offset: 0x0003718F
        // (set) Token: 0x06000E11 RID: 3601 RVA: 0x00038F97 File Offset: 0x00037197
        [DataSourceProperty]
        public MBBindingList<TroopSelectionItemVM> Troops
        {
            get
            {
                return _troops;
            }
            set
            {
                if (value != _troops)
                {
                    _troops = value;
                    base.OnPropertyChangedWithValue<MBBindingList<TroopSelectionItemVM>>(value, "Troops");
                }
            }
        }

        // Token: 0x17000495 RID: 1173
        // (get) Token: 0x06000E12 RID: 3602 RVA: 0x00038FB5 File Offset: 0x000371B5
        // (set) Token: 0x06000E13 RID: 3603 RVA: 0x00038FBD File Offset: 0x000371BD
        [DataSourceProperty]
        public string DoneText
        {
            get
            {
                return _doneText;
            }
            set
            {
                if (value != _doneText)
                {
                    _doneText = value;
                    base.OnPropertyChangedWithValue<string>(value, "DoneText");
                }
            }
        }

        // Token: 0x17000496 RID: 1174
        // (get) Token: 0x06000E14 RID: 3604 RVA: 0x00038FE0 File Offset: 0x000371E0
        // (set) Token: 0x06000E15 RID: 3605 RVA: 0x00038FE8 File Offset: 0x000371E8
        [DataSourceProperty]
        public string CancelText
        {
            get
            {
                return _cancelText;
            }
            set
            {
                if (value != _cancelText)
                {
                    _cancelText = value;
                    base.OnPropertyChangedWithValue<string>(value, "CancelText");
                }
            }
        }

        // Token: 0x17000497 RID: 1175
        // (get) Token: 0x06000E16 RID: 3606 RVA: 0x0003900B File Offset: 0x0003720B
        // (set) Token: 0x06000E17 RID: 3607 RVA: 0x00039013 File Offset: 0x00037213
        [DataSourceProperty]
        public string TitleText
        {
            get
            {
                return _titleText;
            }
            set
            {
                if (value != _titleText)
                {
                    _titleText = value;
                    base.OnPropertyChangedWithValue<string>(value, "TitleText");
                }
            }
        }

        // Token: 0x17000498 RID: 1176
        // (get) Token: 0x06000E18 RID: 3608 RVA: 0x00039036 File Offset: 0x00037236
        // (set) Token: 0x06000E19 RID: 3609 RVA: 0x0003903E File Offset: 0x0003723E
        [DataSourceProperty]
        public string ClearSelectionText
        {
            get
            {
                return _clearSelectionText;
            }
            set
            {
                if (value != _clearSelectionText)
                {
                    _clearSelectionText = value;
                    base.OnPropertyChangedWithValue<string>(value, "ClearSelectionText");
                }
            }
        }

        // Token: 0x17000499 RID: 1177
        // (get) Token: 0x06000E1A RID: 3610 RVA: 0x00039061 File Offset: 0x00037261
        // (set) Token: 0x06000E1B RID: 3611 RVA: 0x00039069 File Offset: 0x00037269
        [DataSourceProperty]
        public string CurrentSelectedAmountText
        {
            get
            {
                return _currentSelectedAmountText;
            }
            set
            {
                if (value != _currentSelectedAmountText)
                {
                    _currentSelectedAmountText = value;
                    base.OnPropertyChangedWithValue<string>(value, "CurrentSelectedAmountText");
                }
            }
        }

        // Token: 0x1700049A RID: 1178
        // (get) Token: 0x06000E1C RID: 3612 RVA: 0x0003908C File Offset: 0x0003728C
        // (set) Token: 0x06000E1D RID: 3613 RVA: 0x00039094 File Offset: 0x00037294
        [DataSourceProperty]
        public string CurrentSelectedAmountTitle
        {
            get
            {
                return _currentSelectedAmountTitle;
            }
            set
            {
                if (value != _currentSelectedAmountTitle)
                {
                    _currentSelectedAmountTitle = value;
                    base.OnPropertyChangedWithValue<string>(value, "CurrentSelectedAmountTitle");
                }
            }
        }

        [DataSourceProperty]
        public int InfantryAmount
        {
            get
            {
                return _infantryAmount;
            }
            set
            {
                if (value != _infantryAmount)
                {
                    _infantryAmount = value;
                    base.OnPropertyChangedWithValue(value, "InfantryAmount");
                }
            }
        }
        [DataSourceProperty]
        public int ArcherAmount
        {
            get
            {
                return _archerAmount;
            }
            set
            {
                if (value != _archerAmount)
                {
                    _archerAmount = value;
                    base.OnPropertyChangedWithValue(value, "ArcherAmount");
                }
            }
        }
        [DataSourceProperty]
        public int CavalryAmount
        {
            get
            {
                return _cavalryAmount;
            }
            set
            {
                if (value != _cavalryAmount)
                {
                    _cavalryAmount = value;
                    base.OnPropertyChangedWithValue(value, "CavalryAmount");
                }
            }
        }
        [DataSourceProperty]
        public int HorseArcherAmount
        {
            get
            {
                return _horseArcherAmount;
            }
            set
            {
                if (value != _horseArcherAmount)
                {
                    _horseArcherAmount = value;
                    base.OnPropertyChangedWithValue(value, "HorseArcherAmount");
                }
            }
        }
        private int _infantryAmount;
        private int _archerAmount;
        private int _cavalryAmount;
        private int _horseArcherAmount;





        [DataSourceProperty]
        public SelectorVM<SelectorItemVM> SortSelector
        {
            get
            {
                return _sortSelector;
            }
            set
            {
                if (value != _sortSelector)
                {
                    _sortSelector = value;
                    base.OnPropertyChangedWithValue<SelectorVM<SelectorItemVM>>(value, "SortSelector");
                }
            }
        }
        private SelectorVM<SelectorItemVM> _sortSelector;

        [DataSourceProperty]
        public string OrderByTierText
        {
            get
            {
                return _OrderByTierText;
            }
            set
            {
                if (value != _OrderByTierText)
                {
                    _OrderByTierText = value;
                    base.OnPropertyChangedWithValue<string>(value, "OrderByTierText");
                }
            }
        }
        private string _OrderByTierText;
        [DataSourceProperty]
        public string OrderByNameText
        {
            get
            {
                return _OrderByNameText;
            }
            set
            {
                if (value != _OrderByNameText)
                {
                    _OrderByNameText = value;
                    base.OnPropertyChangedWithValue<string>(value, "OrderByNameText");
                }
            }
        }
        private string _OrderByNameText;

        [DataSourceProperty]
        public string OrderByCountText
        {
            get
            {
                return _OrderByCountText;
            }
            set
            {
                if (value != _OrderByCountText)
                {
                    _OrderByCountText = value;
                    base.OnPropertyChangedWithValue<string>(value, "OrderByCountText");
                }
            }
        }
        private string _OrderByCountText;

        [DataSourceProperty]
        public string OrderByCultureText
        {
            get
            {
                return _OrderByCultureText;
            }
            set
            {
                if (value != _OrderByCultureText)
                {
                    _OrderByCultureText = value;
                    base.OnPropertyChangedWithValue<string>(value, "OrderByCultureText");
                }
            }
        }
        private string _OrderByCultureText;

        [DataSourceProperty]
        public string OrderByClassText
        {
            get
            {
                return _OrderByClassText;
            }
            set
            {
                if (value != _OrderByClassText)
                {
                    _OrderByClassText = value;
                    base.OnPropertyChangedWithValue<string>(value, "OrderByClassText");
                }
            }
        }
        private string _OrderByClassText;
        // Token: 0x0400067B RID: 1659
        private readonly Action<TroopRoster> _onDone;

        // Token: 0x0400067C RID: 1660
        private readonly TroopRoster _fullRoster;

        // Token: 0x0400067D RID: 1661
        private readonly TroopRoster _initialSelections;

        // Token: 0x0400067E RID: 1662
        private readonly Func<CharacterObject, bool> _canChangeChangeStatusOfTroop;

        // Token: 0x0400067F RID: 1663
        private readonly int _maxSelectableTroopCount;

        // Token: 0x04000680 RID: 1664
        private readonly int _minSelectableTroopCount;

        // Token: 0x04000681 RID: 1665
        private readonly TextObject _titleTextObject = new TextObject("{=cyt_initial_troops}Initial Troops", null);

        // Token: 0x04000682 RID: 1666
        private readonly TextObject _chosenTitleTextObject = new TextObject("{=cyt_chosen_troops}Chosen Troops", null);

        // Token: 0x04000683 RID: 1667
        private int _currentTotalSelectedTroopCount;

        // Token: 0x04000684 RID: 1668
        public bool IsFiveStackModifierActive;

        // Token: 0x04000685 RID: 1669
        public bool IsEntireStackModifierActive;

        // Token: 0x04000686 RID: 1670
        private InputKeyItemVM _doneInputKey;

        // Token: 0x04000687 RID: 1671
        private InputKeyItemVM _cancelInputKey;

        // Token: 0x04000688 RID: 1672
        private InputKeyItemVM _resetInputKey;

        // Token: 0x04000689 RID: 1673
        private bool _isEnabled;

        // Token: 0x0400068A RID: 1674
        private bool _isDoneEnabled;

        // Token: 0x0400068B RID: 1675
        private string _doneText;

        // Token: 0x0400068C RID: 1676
        private string _cancelText;

        // Token: 0x0400068D RID: 1677
        private string _titleText;

        // Token: 0x0400068E RID: 1678
        private string _clearSelectionText;

        // Token: 0x0400068F RID: 1679
        private string _currentSelectedAmountText;

        // Token: 0x04000690 RID: 1680
        private string _currentSelectedAmountTitle;

        // Token: 0x04000691 RID: 1681
        private MBBindingList<TroopSelectionItemVM> _troops;
    }







   // [OverrideView(typeof(MenuTroopSelectionView))]
    public class CYTGauntletMenuTroopSelectionView : MenuView
    {
        // Token: 0x06000138 RID: 312 RVA: 0x00009D10 File Offset: 0x00007F10
        public CYTGauntletMenuTroopSelectionView(TroopRoster fullRoster, TroopRoster initialSelections, Func<CharacterObject, bool> changeChangeStatusOfTroop, Action<TroopRoster> onDone, int maxSelectableTroopCount, int minSelectableTroopCount)
        {
            _onDone = onDone;
            _fullRoster = fullRoster;
            _initialSelections = initialSelections;
            _changeChangeStatusOfTroop = changeChangeStatusOfTroop;
            _maxSelectableTroopCount = maxSelectableTroopCount;
            _minSelectableTroopCount = minSelectableTroopCount;
        }
        public CYTGauntletMenuTroopSelectionView() => throw new NotImplementedException();
        // Token: 0x06000139 RID: 313 RVA: 0x00009D48 File Offset: 0x00007F48
        protected override void OnInitialize()
        {
            base.OnInitialize();
            this._dataSource = new CYTGameMenuTroopSelectionVM(this._fullRoster, this._initialSelections, this._changeChangeStatusOfTroop, new Action<TroopRoster>(this.OnDone), this._maxSelectableTroopCount, this._minSelectableTroopCount)
            {
                IsEnabled = true
            };
            this._dataSource.SetCancelInputKey(HotKeyManager.GetCategory("GenericPanelGameKeyCategory").GetHotKey("Exit"));
            this._dataSource.SetDoneInputKey(HotKeyManager.GetCategory("GenericPanelGameKeyCategory").GetHotKey("Confirm"));
            this._dataSource.SetResetInputKey(HotKeyManager.GetCategory("GenericPanelGameKeyCategory").GetHotKey("Reset"));
            this.Layer = (ScreenLayer) new GauntletLayer("MapTroopSelection", 206);
            this._layerAsGauntletLayer = this.Layer as GauntletLayer;
            this.Layer.InputRestrictions.SetInputRestrictions();
            this.Layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericPanelGameKeyCategory"));
            this.Layer.Input.RegisterHotKeyCategory(HotKeyManager.GetCategory("GenericCampaignPanelsGameKeyCategory"));
            this._movie = this._layerAsGauntletLayer.LoadMovie("GameMenuTroopSelection", (ViewModel) this._dataSource);
            this.Layer.IsFocusLayer = true;
            ScreenManager.TrySetFocus((ScreenLayer) this._layerAsGauntletLayer);
            this.MenuViewContext.AddLayer(this.Layer);
            
            if (!(ScreenManager.TopScreen is MapScreen topScreen))
                return;
            topScreen.SetIsInHideoutTroopManage(true);
        }

        // Token: 0x0600013A RID: 314 RVA: 0x00009EC5 File Offset: 0x000080C5
        private void OnDone(TroopRoster obj)
        {
            MapScreen.Instance.SetIsInHideoutTroopManage(false);
            base.MenuViewContext.CloseTroopSelection();
            Action<TroopRoster> onDone = _onDone;
            if (onDone == null)
            {
                return;
            }
            onDone.DynamicInvokeWithLog(new object[]
            {
                obj
            });
        }

        // Token: 0x0600013B RID: 315 RVA: 0x00009EF8 File Offset: 0x000080F8
        protected override void OnFinalize()
        {
            base.Layer.IsFocusLayer = false;
            ScreenManager.TryLoseFocus(base.Layer);
            _dataSource.OnFinalize();
            _dataSource = null;
            _layerAsGauntletLayer.ReleaseMovie(_movie);
            base.MenuViewContext.RemoveLayer(base.Layer);
            _movie = null;
            base.Layer = null;
            _layerAsGauntletLayer = null;
            MapScreen.Instance.SetIsInHideoutTroopManage(false);
            base.OnFinalize();
        }

        // Token: 0x0600013C RID: 316 RVA: 0x00009F78 File Offset: 0x00008178
        protected override void OnFrameTick(float dt)
        {
            base.OnFrameTick(dt);
            if (_dataSource != null)
            {
                _dataSource.IsFiveStackModifierActive = base.Layer.Input.IsHotKeyDown("FiveStackModifier");
                _dataSource.IsEntireStackModifierActive = base.Layer.Input.IsHotKeyDown("EntireStackModifier");
            }
            ScreenLayer layer = base.Layer;
            if (layer != null && layer.Input.IsHotKeyPressed("Exit"))
            {
                UISoundsHelper.PlayUISound("event:/ui/default");
                _dataSource.ExecuteCancel();
            }
            else
            {
                ScreenLayer layer2 = base.Layer;
                if (layer2 != null && layer2.Input.IsHotKeyPressed("Confirm"))
                {
                    UISoundsHelper.PlayUISound("event:/ui/default");
                    _dataSource.ExecuteDone();
                }
                else
                {
                    ScreenLayer layer3 = base.Layer;
                    if (layer3 != null && layer3.Input.IsHotKeyPressed("Reset"))
                    {
                        UISoundsHelper.PlayUISound("event:/ui/default");
                        _dataSource.ExecuteReset();
                    }
                }
            }
            CYTGameMenuTroopSelectionVM dataSource = _dataSource;
            if (dataSource != null && !dataSource.IsEnabled)
            {
                base.MenuViewContext.CloseTroopSelection();
            }
        }

        // Token: 0x04000084 RID: 132
        private readonly Action<TroopRoster> _onDone;

        // Token: 0x04000085 RID: 133
        private readonly TroopRoster _fullRoster;

        // Token: 0x04000086 RID: 134
        private readonly TroopRoster _initialSelections;

        // Token: 0x04000087 RID: 135
        private readonly Func<CharacterObject, bool> _changeChangeStatusOfTroop;

        // Token: 0x04000088 RID: 136
        private readonly int _maxSelectableTroopCount;

        // Token: 0x04000089 RID: 137
        private readonly int _minSelectableTroopCount;

        // Token: 0x0400008A RID: 138
        private GauntletLayer _layerAsGauntletLayer;

        // Token: 0x0400008B RID: 139
        private CYTGameMenuTroopSelectionVM _dataSource;

        // Token: 0x0400008C RID: 140
        private GauntletMovieIdentifier _movie;
    }

    public class CYTTroopItemComparer : IComparer<TroopSelectionItemVM>
    {
        private static string _orderingType;
        private bool _IsAscending;
        public CYTTroopItemComparer(string orderingType = "default", bool isAscending = true)
        {
            _orderingType = orderingType;
            _IsAscending = isAscending;
        }
        public int Compare(TroopSelectionItemVM x, TroopSelectionItemVM y)
        {
            if (y.Troop.Character.IsPlayerCharacter)
            {
                return 1;
            }

            if (y.Troop.Character.IsHero)
            {
                if (x.Troop.Character.IsPlayerCharacter)
                {
                    return -1;
                }

                if (x.Troop.Character.IsHero)
                {
                    return y.Troop.Character.Level - x.Troop.Character.Level;
                }

                return 1;
            }

            if (x.Troop.Character.IsPlayerCharacter || x.Troop.Character.IsHero)
            {
                return -1;
            }
            int result = 0;
            switch(_orderingType)
            {
                case "tier":
                    result = x.Troop.Character.Tier - y.Troop.Character.Tier;
                    break;
                case "name":
                    result = y.Troop.Character.Name.ToString().CompareTo(x.Troop.Character.Name.ToString());
                    break;
                case "class":
                    result = (int)y.Troop.Character.DefaultFormationClass - (int)x.Troop.Character.DefaultFormationClass;
                    break;
                case "count":
                    result = y.Troop.Number > x.Troop.Number ? -1 : 1;
                    break;
                case "culture":
                    result = y.Troop.Character.Culture.StringId.CompareTo(x.Troop.Character.Culture.StringId);
                    break;
                case "default":
                    result = y.Troop.Character.Level - x.Troop.Character.Level;
                    break;
            }
            if (!_IsAscending)
                result = -result;
            return result;

        }
    }

}
