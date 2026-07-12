namespace F1Game.Core
{
    /// <summary>
    /// Engine-free R&D eligibility/affordability predicates. These MIRROR the
    /// preconditions the authoritative CareerManager mutation methods
    /// (TryUpgradeDepartment / TryStartUpgradeProject / TryReworkProject) check
    /// before deducting resource points and writing the save. They exist so the
    /// production R&D UI can enable/disable buttons and prevent obvious no-op
    /// submissions WITHOUT duplicating the mutation, deduction or save logic - the
    /// CareerManager remains the sole authority and re-validates everything itself.
    /// Costs come from the shared CarDevelopmentRules so the two never diverge.
    /// Pure and unit-testable.
    /// </summary>
    public static class RndEligibility
    {
        /// <summary>Enough slots for another concurrent project?</summary>
        public static bool HasFreeSlot(int activeCount, int maxActive) => activeCount < maxActive;

        /// <summary>The department is high enough for an upgrade of this tier.</summary>
        public static bool MeetsTier(int departmentLevel, int upgradeTier)
        {
            int required = upgradeTier < 1 ? 1 : upgradeTier;
            return departmentLevel >= required;
        }

        /// <summary>Can the department be levelled up (not maxed and affordable)?</summary>
        public static bool CanUpgradeDepartment(int currentLevel, int maxLevel, int resourcePoints)
        {
            if (currentLevel >= maxLevel)
            {
                return false;
            }

            return resourcePoints >= CarDevelopmentRules.DepartmentUpgradeCost(currentLevel);
        }

        /// <summary>
        /// Can a new upgrade project start? Mirrors TryStartUpgradeProject: not already
        /// owned/failed/in-progress, prerequisite met, department tier met, a free slot,
        /// and affordable at <paramref name="projectCost"/>.
        /// </summary>
        public static bool CanStartProject(int resourcePoints, int projectCost, int departmentLevel, int upgradeTier,
            int activeCount, int maxActive, bool alreadyOwnedOrActive, bool prerequisiteMet)
        {
            if (alreadyOwnedOrActive || !prerequisiteMet)
            {
                return false;
            }

            if (!MeetsTier(departmentLevel, upgradeTier) || !HasFreeSlot(activeCount, maxActive))
            {
                return false;
            }

            return resourcePoints >= projectCost;
        }

        /// <summary>
        /// Can a failed project be reworked? Mirrors TryReworkProject: it is in the
        /// reworkable state, a free slot exists, and the rework cost is affordable.
        /// </summary>
        public static bool CanRework(int resourcePoints, int originalProjectCost, int activeCount, int maxActive, bool isReworkable)
        {
            if (!isReworkable || !HasFreeSlot(activeCount, maxActive))
            {
                return false;
            }

            return resourcePoints >= CarDevelopmentRules.ReworkCost(originalProjectCost);
        }
    }
}
