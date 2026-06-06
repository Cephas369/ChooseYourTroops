        public void ExecuteSelectAll()
        {
            ExecuteReset();

            FormationClass[] formationClasses = new[]
            {
                FormationClass.Infantry,
                FormationClass.Ranged,
                FormationClass.Cavalry,
                FormationClass.HorseArcher
            };

            List<FormationClass> activeFormationClasses = formationClasses
                .Where(HasSelectableTroopsForFormationClass)
                .ToList();

            if (activeFormationClasses.Count == 0)
                return;

            // Loop up to the maximum selectable count to ensure maximum selection
            for (int round = 0; round < _maxSelectableTroopCount; round++)
            {
                List<TroopSelectionItemVM> troopsToAdd = new(activeFormationClasses.Count);
                int roundCost = 0;

                foreach (FormationClass formationClass in activeFormationClasses)
                {
                    TroopSelectionItemVM troopToAdd = GetSelectableTroopForFormationClass(formationClass);
                    if (troopToAdd == null)
                        return;

                    int troopCost = GetTroopCost(troopToAdd);
                    if (_currentTotalSelectedTroopCount + roundCost + troopCost > _maxSelectableTroopCount)
                        return;

                    troopsToAdd.Add(troopToAdd);
                    roundCost += troopCost;
                }

                foreach (TroopSelectionItemVM troopSelectionItemVM in troopsToAdd)
                {
                    OnAddCount(troopSelectionItemVM);
                }
            }
        }
