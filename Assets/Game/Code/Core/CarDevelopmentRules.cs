using UnityEngine;

namespace F1Game.Core
{
    /// <summary>
    /// Pure car-development (R&D) project maths: how a chosen risk level and the
    /// department's level shift a project's success chance, duration and cost.
    /// The career layer owns the project state, the random rolls and the reward
    /// application; it asks these rules for the deterministic numbers so they stay
    /// testable. Risk modes match CareerManager's constants (0 conservative,
    /// 1 standard, 2 rush, 3 experimental).
    /// </summary>
    public static class CarDevelopmentRules
    {
        public const int RiskConservative = 0;
        public const int RiskStandard = 1;
        public const int RiskRush = 2;
        public const int RiskExperimental = 3;

        // Cost to raise a department one level scales with its current level.
        public const int DepartmentUpgradeCostPerLevel = 400;

        /// <summary>
        /// Success probability for a project: the upgrade's base chance, nudged up
        /// by department level, then shifted by the risk mode (conservative is
        /// safer, rush/experimental gamble for a bigger/faster result). Clamped to
        /// a sane [0.05, 0.97] band.
        /// </summary>
        public static float SuccessChance(float baseChance, int departmentLevel, int riskMode)
        {
            float chance = baseChance;
            chance += 0.04f * (departmentLevel - 1);
            if (riskMode == RiskConservative)
            {
                chance = Mathf.Min(0.97f, chance + 0.10f);
            }
            else
            {
                if (riskMode == RiskRush)
                {
                    chance -= 0.15f;
                }
                else if (riskMode == RiskExperimental)
                {
                    chance -= 0.12f;
                }

                chance = Mathf.Min(0.95f, chance);
            }

            return Mathf.Max(0.05f, chance);
        }

        /// <summary>
        /// Weeks a project takes: derived from its development-days budget, shortened
        /// by a higher department level, then stretched (conservative) or compressed
        /// (rush) by the risk mode. Always at least one week.
        /// </summary>
        public static int DevelopmentWeeks(int developmentDays, int departmentLevel, int riskMode)
        {
            int weeks = Mathf.Max(1, Mathf.CeilToInt(developmentDays / 10f));
            if (departmentLevel > 1)
            {
                weeks = Mathf.Max(1, Mathf.CeilToInt(weeks * (1f - 0.1f * (departmentLevel - 1))));
            }

            if (riskMode == RiskConservative)
            {
                weeks = Mathf.CeilToInt(weeks * 1.25f);
            }
            else if (riskMode == RiskRush)
            {
                weeks = Mathf.Max(1, Mathf.RoundToInt(weeks * 0.65f));
            }

            return weeks;
        }

        /// <summary>Project cost: conservative development carries a 15% premium.</summary>
        public static int Cost(int baseCost, int riskMode)
        {
            return riskMode == RiskConservative ? Mathf.RoundToInt(baseCost * 1.15f) : baseCost;
        }

        /// <summary>Cost to raise a department from its current level by one.</summary>
        public static int DepartmentUpgradeCost(int currentLevel)
        {
            return currentLevel * DepartmentUpgradeCostPerLevel;
        }
    }
}
