using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Base.Global;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Text.RegularExpressions;
using MCM.Common;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;

namespace ChooseYourTroops
{
    internal sealed class ChooseYourTroopsConfig : AttributeGlobalSettings<ChooseYourTroopsConfig> 
    {
        private static readonly Dictionary<int, int> battleSizes = new()
            { { 0, 200 }, { 1, 300 }, { 2, 400 }, { 3, 500 }, { 4, 600 }, { 5, 800 }, { 6, 1000 } };

        public static int CurrentBattleSizeLimit => Instance is { SupportForMaximumTroops: true }
            ? 2040
            : battleSizes[BannerlordConfig.BattleSize];
        
        public override string Id => "choose_your_troops";
        public override string DisplayName => $"Choose Your Troops";
        public override string FolderName => "ChooseYourTroops";
        public override string FormatType => "json";

        /*[SettingPropertyGroup("{=general}General", GroupOrder = 1)]
        [SettingPropertyBool("{=disable_troop_balancing_option}Disable troop balancing", Order = 1, RequireRestart = false)]
        public bool DisableArmyBalance { get; set; } = false;*/
        
        [SettingPropertyGroup("{=general}General", GroupOrder = 1)]
        [SettingPropertyBool("{=max_battle_size_support_option}Support for maximum battle size mods (READ DESCRIPTION)", Order = 1, RequireRestart = false, HintText = "{=max_battle_size_support_option_description}Allows to choose the maximum troops allowed by engine in battle (2040), another mod is required to handle it in battle like BattleSize Resized.")]
        public bool SupportForMaximumTroops { get; set; } = false;
    }
}
