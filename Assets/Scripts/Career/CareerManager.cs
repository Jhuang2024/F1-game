using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    public class CareerManager
    {
        const string CareerFile = "formula_racing_career.json";
        static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };
        const float UpgradeEffectScale = 2.25f;
        const float ExperimentalBonusScale = 1.3f;

        // Order matters: index into CareerSaveData.departmentLevels, names match
        // the upgrade `category` strings in upgrades.json exactly.
        public static readonly string[] DepartmentNames =
        {
            "Aerodynamics", "Chassis", "Power Unit", "Durability", "Tyre Management", "ERS"
        };

        public const int MaxDepartmentLevel = 5;
        public const int RiskConservative = 0;
        public const int RiskStandard = 1;
        public const int RiskRush = 2;
        public const int RiskExperimental = 3;
        public const int ProjectInDevelopment = 0;
        public const int ProjectCompleted = 1;
        public const int ProjectFailed = 2;
        public const int ProjectReworkAvailable = 3;

        readonly GameDataRepository data;

        public CareerSaveData Save { get; private set; }

        public CareerManager(GameDataRepository repository)
        {
            data = repository;
            Save = LocalJsonStore.Load<CareerSaveData>(CareerFile, null);
            if (Save == null || string.IsNullOrEmpty(Save.playerTeamId))
            {
                StartNewCareer("Player Driver", "williams");
            }
            else
            {
                EnsureStandingLists();
            }
        }

        public void StartNewCareer(string driverName, string teamId)
        {
            StartNewCareer(driverName, teamId, false, "");
        }

        public void StartNewCareer(string driverName, string teamId, bool useExistingDriver, string selectedDriverId)
        {
            if (string.IsNullOrEmpty(driverName))
            {
                driverName = "Player Driver";
            }

            // Bug fix: this used to gate everything on the `useExistingDriver` flag
            // at the moment StartNewCareer runs rather than on whether a real
            // driver was actually resolved. Toggling the existing/custom mode
            // button after picking a real driver (selectedDriverId stays set,
            // useExistingDriver flips to false) used to leave that real driver
            // fully in the AI roster while the player's name/team still matched
            // them exactly - a visible duplicate (same name racing as both the
            // player and an AI car). Keying off `selected != null` instead means
            // "a real driver was actually picked" is the only thing that matters,
            // regardless of how the toggle happens to be sitting. Guarding the
            // FindDriver call against an empty id avoids its own fallback
            // (returns some default driver rather than null for an unresolved id)
            // ever masquerading as a real pick when the player never chose one.
            DriverData selected = string.IsNullOrEmpty(selectedDriverId) ? null : data.FindDriver(selectedDriverId);
            if (selected != null)
            {
                driverName = selected.displayName;
                teamId = selected.teamId;
            }

            Save = new CareerSaveData
            {
                currentSeason = 1,
                currentRound = 1,
                playerDriverName = driverName,
                playerTeamId = teamId,
                useExistingDriver = selected != null,
                selectedDriverId = selected != null ? selected.id : "",
                rivalDriverId = PickRivalId(teamId, selectedDriverId),
                contractTargetPosition = ContractTargetForTeam(teamId),
                reputation = 25,
                resourcePoints = 500,
                difficultyIndex = 1,
                driverStandings = data.CreateInitialDriverStandings(driverName, teamId, selected != null ? selected.id : ""),
                constructorStandings = data.CreateInitialConstructorStandings()
            };
            if (selected != null)
            {
                Save.driverStandings.RemoveAll(entry => entry.id == selected.id);
            }
            EnsureRndState();
            Write();
        }

        public void Write()
        {
            LocalJsonStore.Save(CareerFile, Save);
        }

        public CalendarEventData CurrentEvent()
        {
            return data.FindEventForRound(Save.currentRound);
        }

        public bool HasQualifyingForCurrentRound()
        {
            if (Save == null || Save.qualifyingResults == null)
            {
                return false;
            }

            for (int i = 0; i < Save.qualifyingResults.Count; i++)
            {
                QualifyingResultRecord record = Save.qualifyingResults[i];
                if (record.season == Save.currentSeason && record.round == Save.currentRound && record.results != null && record.results.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public CarPerformanceData GetPlayerCar()
        {
            TeamData team = data.FindTeam(Save.playerTeamId);
            return team == null ? data.Cars.cars[0] : data.FindCar(team.carPerformanceId);
        }

        public void ApplyRaceResults(CalendarEventData raceEvent, List<RaceResultEntry> results)
        {
            for (int i = 0; i < results.Count; i++)
            {
                int points = i < Points.Length ? Points[i] : 0;
                results[i].finishingPosition = i + 1;
                results[i].points = points;
                ApplyDriverPoints(results[i], points);
                ApplyConstructorPoints(results[i].teamId, points, results[i].finishingPosition);
            }

            RaceResultRecord record = new RaceResultRecord
            {
                season = Save.currentSeason,
                round = Save.currentRound,
                eventName = raceEvent != null ? raceEvent.displayName : "Prototype GP",
                results = results
            };
            Save.raceResults.Add(record);

            RaceResultEntry player = results.Find(entry => entry.isPlayer);
            if (player != null)
            {
                Save.resourcePoints += 90 + Mathf.Max(0, 11 - player.finishingPosition) * 15;
                int targetDelta = Mathf.Max(-4, Save.contractTargetPosition - player.finishingPosition);
                Save.reputation += player.finishingPosition <= 3 ? 4 : (targetDelta >= 0 ? 2 : -1);
                Save.resourcePoints += Mathf.Max(0, targetDelta) * 12;
                UpdateRaceRivalryAndForm(results, player, raceEvent);
            }

            AdvanceUpgradeProjects();

            Save.currentRound++;
            if (Save.currentRound > data.Calendar.events.Count)
            {
                Save.currentRound = 1;
                Save.currentSeason++;
                ApplyRegulationReset();
            }

            Save.lastQualifyingResults = new List<QualifyingResultEntry>();

            SortStandings(Save.driverStandings);
            SortStandings(Save.constructorStandings);
            Write();
        }

        public void ApplyQualifyingResults(CalendarEventData raceEvent, List<QualifyingResultEntry> results)
        {
            Save.lastQualifyingResults = results;
            Save.qualifyingResults.Add(new QualifyingResultRecord
            {
                season = Save.currentSeason,
                round = Save.currentRound,
                eventName = raceEvent != null ? raceEvent.displayName : "Prototype GP",
                results = results
            });

            QualifyingResultEntry player = results.Find(entry => entry.isPlayer);
            if (player != null)
            {
                Save.resourcePoints += Mathf.Max(8, 42 - player.position * 4);
                Save.reputation += player.position <= Save.contractTargetPosition ? 1 : 0;
                UpdateQualifyingRivalry(results, player);
            }

            Write();
        }

        // Part 3: rivalry / teammate battle. Head-to-head tallies, last-3-race
        // form, a reputation snapshot for the trend card, and a handful of
        // headlines for the career news feed - all derived from this race's
        // actual classification, not scripted.
        void UpdateRaceRivalryAndForm(List<RaceResultEntry> results, RaceResultEntry player, CalendarEventData raceEvent)
        {
            string eventName = raceEvent != null ? raceEvent.displayName : "the race";
            RaceResultEntry teammate = results.Find(entry => entry.teamId == Save.playerTeamId && entry.driverId != "player");
            if (teammate != null)
            {
                if (player.finishingPosition < teammate.finishingPosition)
                {
                    Save.teammateRaceWins++;
                    AddNews(Save.playerDriverName + " beat their teammate again at " + eventName + ".");
                }
                else if (player.finishingPosition > teammate.finishingPosition)
                {
                    Save.teammateRaceLosses++;
                }
            }

            RaceResultEntry rival = string.IsNullOrEmpty(Save.rivalDriverId) ? null : results.Find(entry => entry.driverId == Save.rivalDriverId);
            if (rival != null)
            {
                if (player.finishingPosition < rival.finishingPosition)
                {
                    Save.rivalRaceWins++;
                }
                else if (player.finishingPosition > rival.finishingPosition)
                {
                    Save.rivalRaceLosses++;
                    AddNews(rival.driverName + " got the better of " + Save.playerDriverName + " at " + eventName + ".");
                }
            }

            if (player.finishingPosition == 1)
            {
                AddNews(Save.playerDriverName + " wins at " + eventName + "!");
            }
            else if (player.finishingPosition <= 3)
            {
                AddNews(Save.playerDriverName + " takes a podium finish at " + eventName + ".");
            }

            Save.recentFormPositions.Add(player.finishingPosition);
            while (Save.recentFormPositions.Count > 3)
            {
                Save.recentFormPositions.RemoveAt(0);
            }

            int reputationBefore = Save.reputationHistory.Count > 0 ? Save.reputationHistory[Save.reputationHistory.Count - 1] : Save.reputation;
            if (Save.reputation - reputationBefore >= 3)
            {
                AddNews("Team confidence increased after a strong result at " + eventName + ".");
            }

            Save.reputationHistory.Add(Save.reputation);
            while (Save.reputationHistory.Count > 6)
            {
                Save.reputationHistory.RemoveAt(0);
            }

            Save.roundsSinceRivalPicked++;
            if (Save.roundsSinceRivalPicked >= 4)
            {
                ReevaluateRival();
            }
        }

        void UpdateQualifyingRivalry(List<QualifyingResultEntry> results, QualifyingResultEntry player)
        {
            QualifyingResultEntry teammate = results.Find(entry => entry.teamId == Save.playerTeamId && entry.driverId != "player");
            if (teammate != null)
            {
                if (player.position < teammate.position)
                {
                    Save.teammateQualifyingWins++;
                }
                else if (player.position > teammate.position)
                {
                    Save.teammateQualifyingLosses++;
                }
            }

            QualifyingResultEntry rival = string.IsNullOrEmpty(Save.rivalDriverId) ? null : results.Find(entry => entry.driverId == Save.rivalDriverId);
            if (rival != null)
            {
                if (player.position < rival.position)
                {
                    Save.rivalQualifyingWins++;
                    if (player.position <= 3)
                    {
                        AddNews(Save.playerDriverName + " out-qualified their rival again at Q" + player.position + ".");
                    }
                }
                else if (player.position > rival.position)
                {
                    Save.rivalQualifyingLosses++;
                }
            }
        }

        // Re-evaluates the rival every few rounds: prefers the nearest
        // championship rival by points that isn't the player or their teammate;
        // falls back to the teammate if nobody else is close. Keeps the rival
        // fixed if the current pick is still the closest match, so it doesn't
        // flip every cycle for no reason.
        void ReevaluateRival()
        {
            Save.roundsSinceRivalPicked = 0;
            StandingEntry playerStanding = Save.driverStandings.Find(entry => entry.id == "player");
            if (playerStanding == null)
            {
                return;
            }

            StandingEntry closest = null;
            int closestGap = int.MaxValue;
            for (int i = 0; i < Save.driverStandings.Count; i++)
            {
                StandingEntry candidate = Save.driverStandings[i];
                if (candidate.id == "player" || candidate.teamId == Save.playerTeamId)
                {
                    continue;
                }

                int gap = Mathf.Abs(candidate.points - playerStanding.points);
                if (gap < closestGap)
                {
                    closestGap = gap;
                    closest = candidate;
                }
            }

            if (closest == null)
            {
                // No other team on the grid - fall back to the teammate.
                List<DriverData> teamDrivers = data.GetDriversForTeam(Save.playerTeamId);
                DriverData teammateDriver = teamDrivers.Find(driver => driver.id != Save.selectedDriverId);
                if (teammateDriver != null)
                {
                    Save.rivalDriverId = teammateDriver.id;
                }

                return;
            }

            if (closest.id != Save.rivalDriverId)
            {
                Save.rivalDriverId = closest.id;
                Save.rivalRaceWins = 0;
                Save.rivalRaceLosses = 0;
                Save.rivalQualifyingWins = 0;
                Save.rivalQualifyingLosses = 0;
                AddNews("New championship rival: " + closest.displayName + " is now " + Save.playerDriverName + "'s closest title threat.");
            }
        }

        public void AddNews(string headline)
        {
            if (string.IsNullOrEmpty(headline) || Save.newsFeed == null)
            {
                return;
            }

            Save.newsFeed.Add(headline);
            while (Save.newsFeed.Count > 10)
            {
                Save.newsFeed.RemoveAt(0);
            }
        }

        public bool TryPurchaseUpgrade(string upgradeId)
        {
            return TryStartUpgradeProject(upgradeId, RiskStandard);
        }

        public bool TryStartUpgradeProject(string upgradeId, int riskMode)
        {
            UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == upgradeId);
            if (upgrade == null || Save.completedUpgradeIds.Contains(upgradeId) || Save.failedUpgradeIds.Contains(upgradeId))
            {
                return false;
            }

            if (FindProject(upgradeId) != null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(upgrade.requiredUpgradeId) && !Save.completedUpgradeIds.Contains(upgrade.requiredUpgradeId))
            {
                return false;
            }

            if (GetDepartmentLevel(GetDepartmentIndex(upgrade.category)) < Mathf.Max(1, upgrade.tier))
            {
                return false;
            }

            if (ActiveProjectCount() >= MaxActiveProjects())
            {
                return false;
            }

            int cost = ComputeProjectCost(upgrade, riskMode);
            if (Save.resourcePoints < cost)
            {
                return false;
            }

            int weeks = ComputeProjectWeeks(upgrade, riskMode);
            Save.resourcePoints -= cost;
            Save.activeUpgradeProjects.Add(new ActiveUpgradeProject
            {
                upgradeId = upgradeId,
                category = upgrade.category,
                startRound = Save.currentRound,
                remainingRaceWeeks = weeks,
                totalRaceWeeks = weeks,
                cost = cost,
                successChance = ComputeProjectSuccessChance(upgrade, riskMode),
                riskMode = riskMode,
                status = ProjectInDevelopment
            });
            Write();
            return true;
        }

        public bool TryReworkProject(string upgradeId)
        {
            ActiveUpgradeProject project = FindProject(upgradeId);
            if (project == null || project.status != ProjectReworkAvailable)
            {
                return false;
            }

            if (ActiveProjectCount() >= MaxActiveProjects())
            {
                return false;
            }

            int cost = GetReworkCost(project);
            if (Save.resourcePoints < cost)
            {
                return false;
            }

            Save.resourcePoints -= cost;
            project.status = ProjectInDevelopment;
            project.startRound = Save.currentRound;
            project.totalRaceWeeks = Mathf.Max(1, project.totalRaceWeeks / 2);
            project.remainingRaceWeeks = project.totalRaceWeeks;
            project.successChance = Mathf.Min(0.95f, project.successChance + 0.12f);
            Write();
            return true;
        }

        public void AbandonProject(string upgradeId)
        {
            ActiveUpgradeProject project = FindProject(upgradeId);
            if (project == null || project.status != ProjectReworkAvailable)
            {
                return;
            }

            Save.activeUpgradeProjects.Remove(project);
            if (!Save.failedUpgradeIds.Contains(upgradeId))
            {
                Save.failedUpgradeIds.Add(upgradeId);
            }

            Write();
        }

        public int GetReworkCost(ActiveUpgradeProject project)
        {
            return Mathf.RoundToInt(project.cost * 0.4f);
        }

        public ActiveUpgradeProject FindProject(string upgradeId)
        {
            if (Save.activeUpgradeProjects == null)
            {
                return null;
            }

            return Save.activeUpgradeProjects.Find(item => item.upgradeId == upgradeId);
        }

        public int ActiveProjectCount()
        {
            int count = 0;
            for (int i = 0; i < Save.activeUpgradeProjects.Count; i++)
            {
                if (Save.activeUpgradeProjects[i].status == ProjectInDevelopment)
                {
                    count++;
                }
            }

            return count;
        }

        // Base 2 slots; every two facility levels bought across the six
        // departments frees one more engineering slot, capped at 5.
        public int MaxActiveProjects()
        {
            int extraLevels = 0;
            for (int i = 0; i < Save.departmentLevels.Count; i++)
            {
                extraLevels += Mathf.Max(0, Save.departmentLevels[i] - 1);
            }

            return Mathf.Min(5, 2 + extraLevels / 2);
        }

        public int GetDepartmentIndex(string category)
        {
            for (int i = 0; i < DepartmentNames.Length; i++)
            {
                if (DepartmentNames[i] == category)
                {
                    return i;
                }
            }

            return -1;
        }

        public int GetDepartmentLevel(int departmentIndex)
        {
            if (departmentIndex < 0 || Save.departmentLevels == null || departmentIndex >= Save.departmentLevels.Count)
            {
                return 1;
            }

            return Save.departmentLevels[departmentIndex];
        }

        public int GetDepartmentUpgradeCost(int departmentIndex)
        {
            return GetDepartmentLevel(departmentIndex) * 400;
        }

        public bool TryUpgradeDepartment(int departmentIndex)
        {
            if (departmentIndex < 0 || departmentIndex >= Save.departmentLevels.Count)
            {
                return false;
            }

            int level = Save.departmentLevels[departmentIndex];
            if (level >= MaxDepartmentLevel)
            {
                return false;
            }

            int cost = level * 400;
            if (Save.resourcePoints < cost)
            {
                return false;
            }

            Save.resourcePoints -= cost;
            Save.departmentLevels[departmentIndex] = level + 1;
            Write();
            return true;
        }

        public int ComputeProjectWeeks(UpgradeData upgrade, int riskMode)
        {
            int weeks = Mathf.Max(1, Mathf.CeilToInt(upgrade.developmentDays / 10f));
            int level = GetDepartmentLevel(GetDepartmentIndex(upgrade.category));
            if (level > 1)
            {
                weeks = Mathf.Max(1, Mathf.CeilToInt(weeks * (1f - 0.1f * (level - 1))));
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

        public float ComputeProjectSuccessChance(UpgradeData upgrade, int riskMode)
        {
            float chance = upgrade.successChance;
            int level = GetDepartmentLevel(GetDepartmentIndex(upgrade.category));
            chance += 0.04f * (level - 1);
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

        public int ComputeProjectCost(UpgradeData upgrade, int riskMode)
        {
            if (riskMode == RiskConservative)
            {
                return Mathf.RoundToInt(upgrade.cost * 1.15f);
            }

            return upgrade.cost;
        }

        public void AdvanceUpgradeProjects()
        {
            float practiceBonus = Save.practiceQualityThisRound > 0 ? 0.03f : 0f;
            for (int i = 0; i < Save.activeUpgradeProjects.Count; i++)
            {
                ActiveUpgradeProject project = Save.activeUpgradeProjects[i];
                if (project.status != ProjectInDevelopment)
                {
                    continue;
                }

                project.remainingRaceWeeks--;
                if (project.remainingRaceWeeks > 0)
                {
                    continue;
                }

                project.remainingRaceWeeks = 0;
                string projectId = project.upgradeId;
                UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == projectId);
                string projectName = upgrade != null ? upgrade.displayName : projectId;
                float chance = Mathf.Min(0.97f, project.successChance + practiceBonus);
                if (Random.value <= chance)
                {
                    project.status = ProjectCompleted;
                    if (!Save.completedUpgradeIds.Contains(projectId))
                    {
                        Save.completedUpgradeIds.Add(projectId);
                    }

                    Save.reputation += 1;
                    if (project.riskMode == RiskExperimental && Random.value <= 0.25f)
                    {
                        project.bonusApplied = true;
                        Save.pendingRndMessages.Add(projectName + " delivered with an experimental breakthrough - effect boosted 30%.");
                        AddNews(projectName + " lands with an experimental breakthrough - big step for the team.");
                    }
                    else
                    {
                        Save.pendingRndMessages.Add(projectName + " development complete - fitted to the car.");
                        AddNews(projectName + " development complete, fitted to the car for the next round.");
                    }
                }
                else
                {
                    project.status = ProjectReworkAvailable;
                    Save.pendingRndMessages.Add(projectName + " development failed - rework available at 40% cost.");
                }
            }

            Save.practiceQualityThisRound = 0;
        }

        public CarPerformanceData ApplyCareerUpgrades(CarPerformanceData baseCar)
        {
            CarPerformanceData tuned = new CarPerformanceData
            {
                id = baseCar.id,
                topSpeed = baseCar.topSpeed,
                acceleration = baseCar.acceleration,
                cornering = baseCar.cornering,
                braking = baseCar.braking,
                reliability = baseCar.reliability,
                ersEfficiency = baseCar.ersEfficiency,
                tyreManagement = baseCar.tyreManagement,
                aeroEfficiency = baseCar.aeroEfficiency,
                chassisBalance = baseCar.chassisBalance,
                enginePower = baseCar.enginePower
            };

            for (int i = 0; i < Save.completedUpgradeIds.Count; i++)
            {
                UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == Save.completedUpgradeIds[i]);
                if (upgrade == null)
                {
                    continue;
                }

                ActiveUpgradeProject project = FindProject(upgrade.id);
                float bonus = project != null && project.bonusApplied ? ExperimentalBonusScale : 1f;

                tuned.topSpeed += Mathf.RoundToInt(upgrade.topSpeedDelta * 1.7f * bonus);
                tuned.acceleration += Mathf.RoundToInt(upgrade.accelerationDelta * UpgradeEffectScale * bonus);
                tuned.cornering += Mathf.RoundToInt(upgrade.corneringDelta * UpgradeEffectScale * bonus);
                tuned.braking += Mathf.RoundToInt(upgrade.brakingDelta * UpgradeEffectScale * bonus);
                tuned.reliability += Mathf.RoundToInt(upgrade.reliabilityDelta * 1.6f * bonus);
                tuned.ersEfficiency += Mathf.RoundToInt(upgrade.ersDelta * UpgradeEffectScale * bonus);
                tuned.tyreManagement += Mathf.RoundToInt(upgrade.tyreDelta * UpgradeEffectScale * bonus);
                tuned.aeroEfficiency += Mathf.RoundToInt(upgrade.aeroDelta * UpgradeEffectScale * bonus);
                tuned.chassisBalance += Mathf.RoundToInt(upgrade.chassisDelta * UpgradeEffectScale * bonus);
                tuned.enginePower += Mathf.RoundToInt(upgrade.engineDelta * UpgradeEffectScale * bonus);
            }

            tuned.topSpeed = Mathf.Clamp(tuned.topSpeed, 315, 360);
            tuned.acceleration = Mathf.Clamp(tuned.acceleration, 45, 125);
            tuned.cornering = Mathf.Clamp(tuned.cornering, 45, 125);
            tuned.braking = Mathf.Clamp(tuned.braking, 45, 125);
            tuned.reliability = Mathf.Clamp(tuned.reliability, 35, 125);
            tuned.ersEfficiency = Mathf.Clamp(tuned.ersEfficiency, 45, 125);
            tuned.tyreManagement = Mathf.Clamp(tuned.tyreManagement, 45, 125);
            tuned.aeroEfficiency = Mathf.Clamp(tuned.aeroEfficiency, 45, 125);
            tuned.chassisBalance = Mathf.Clamp(tuned.chassisBalance, 45, 125);
            tuned.enginePower = Mathf.Clamp(tuned.enginePower, 45, 125);
            return tuned;
        }

        void ApplyDriverPoints(RaceResultEntry result, int points)
        {
            StandingEntry entry = Save.driverStandings.Find(item => item.id == result.driverId);
            if (entry == null)
            {
                entry = new StandingEntry
                {
                    id = result.driverId,
                    displayName = result.driverName,
                    teamId = result.teamId
                };
                Save.driverStandings.Add(entry);
            }

            entry.points += points;
            if (result.finishingPosition == 1)
            {
                entry.wins++;
            }

            if (result.finishingPosition <= 3)
            {
                entry.podiums++;
            }
        }

        void ApplyConstructorPoints(string teamId, int points, int position)
        {
            StandingEntry entry = Save.constructorStandings.Find(item => item.id == teamId);
            TeamData team = data.FindTeam(teamId);
            if (entry == null)
            {
                entry = new StandingEntry
                {
                    id = teamId,
                    displayName = team == null ? teamId : team.name,
                    teamId = teamId
                };
                Save.constructorStandings.Add(entry);
            }

            entry.points += points;
            if (position == 1)
            {
                entry.wins++;
            }

            if (position <= 3)
            {
                entry.podiums++;
            }
        }

        void SortStandings(List<StandingEntry> standings)
        {
            standings.Sort((a, b) =>
            {
                int pointsCompare = b.points.CompareTo(a.points);
                if (pointsCompare != 0)
                {
                    return pointsCompare;
                }

                int winsCompare = b.wins.CompareTo(a.wins);
                if (winsCompare != 0)
                {
                    return winsCompare;
                }

                return b.podiums.CompareTo(a.podiums);
            });
        }

        void EnsureStandingLists()
        {
            if (Save.driverStandings == null || Save.driverStandings.Count == 0)
            {
                Save.driverStandings = data.CreateInitialDriverStandings(Save.playerDriverName, Save.playerTeamId, Save.selectedDriverId);
            }

            if (Save.constructorStandings == null || Save.constructorStandings.Count == 0)
            {
                Save.constructorStandings = data.CreateInitialConstructorStandings();
            }

            if (Save.raceResults == null)
            {
                Save.raceResults = new List<RaceResultRecord>();
            }

            if (Save.qualifyingResults == null)
            {
                Save.qualifyingResults = new List<QualifyingResultRecord>();
            }

            if (Save.lastQualifyingResults == null)
            {
                Save.lastQualifyingResults = new List<QualifyingResultEntry>();
            }

            if (string.IsNullOrEmpty(Save.rivalDriverId))
            {
                Save.rivalDriverId = PickRivalId(Save.playerTeamId, Save.selectedDriverId);
            }

            if (Save.contractTargetPosition <= 0)
            {
                Save.contractTargetPosition = ContractTargetForTeam(Save.playerTeamId);
            }

            EnsureRndState();
            EnsurePlayerReplacesDriverSeat();
        }

        // Backwards-compatible defaults for the R&D fields: JsonUtility leaves
        // list fields missing from an old save file as the field initializer,
        // but a save written by an older build can still deserialize with nulls
        // when the whole object graph predates these fields.
        void EnsureRndState()
        {
            if (Save.completedUpgradeIds == null)
            {
                Save.completedUpgradeIds = new List<string>();
            }

            if (Save.failedUpgradeIds == null)
            {
                Save.failedUpgradeIds = new List<string>();
            }

            if (Save.activeUpgradeProjects == null)
            {
                Save.activeUpgradeProjects = new List<ActiveUpgradeProject>();
            }

            if (Save.pendingRndMessages == null)
            {
                Save.pendingRndMessages = new List<string>();
            }

            if (Save.departmentLevels == null)
            {
                Save.departmentLevels = new List<int>();
            }

            while (Save.departmentLevels.Count < DepartmentNames.Length)
            {
                Save.departmentLevels.Add(1);
            }

            for (int i = 0; i < Save.departmentLevels.Count; i++)
            {
                Save.departmentLevels[i] = Mathf.Clamp(Save.departmentLevels[i], 1, MaxDepartmentLevel);
            }

            if (Save.regulationAffectedCategories == null)
            {
                Save.regulationAffectedCategories = new List<string>();
            }

            if (Save.regulationAffectedCategories.Count == 0)
            {
                PickRegulationTargets();
            }

            if (Save.recentFormPositions == null)
            {
                Save.recentFormPositions = new List<int>();
            }

            if (Save.reputationHistory == null)
            {
                Save.reputationHistory = new List<int>();
            }

            if (Save.newsFeed == null)
            {
                Save.newsFeed = new List<string>();
            }
        }

        void PickRegulationTargets()
        {
            Save.regulationAffectedCategories = new List<string>();
            int count = Random.value < 0.5f ? 1 : 2;
            List<string> pool = new List<string>(DepartmentNames);
            for (int i = 0; i < count; i++)
            {
                int index = Random.Range(0, pool.Count);
                Save.regulationAffectedCategories.Add(pool[index]);
                pool.RemoveAt(index);
            }
        }

        void EnsurePlayerReplacesDriverSeat()
        {
            if (Save.driverStandings == null)
            {
                return;
            }

            StandingEntry player = Save.driverStandings.Find(entry => entry.id == "player");
            if (player == null)
            {
                Save.driverStandings.Add(new StandingEntry
                {
                    id = "player",
                    displayName = Save.playerDriverName,
                    teamId = Save.playerTeamId
                });
            }
            else
            {
                player.displayName = Save.playerDriverName;
                player.teamId = Save.playerTeamId;
            }

            string replacedDriverId = Save.selectedDriverId;
            if (string.IsNullOrEmpty(replacedDriverId))
            {
                List<DriverData> teamDrivers = data.GetDriversForTeam(Save.playerTeamId);
                if (teamDrivers.Count > 0)
                {
                    replacedDriverId = teamDrivers[0].id;
                }
            }

            if (!string.IsNullOrEmpty(replacedDriverId))
            {
                Save.driverStandings.RemoveAll(entry => entry.id == replacedDriverId);
            }

            while (Save.driverStandings.Count > 22)
            {
                int removeIndex = Save.driverStandings.FindLastIndex(entry => entry.id != "player");
                if (removeIndex < 0)
                {
                    break;
                }

                Save.driverStandings.RemoveAt(removeIndex);
            }
        }

        void ApplyRegulationReset()
        {
            EnsureRndState();
            int removed = 0;
            for (int i = Save.completedUpgradeIds.Count - 1; i >= 0; i--)
            {
                string upgradeId = Save.completedUpgradeIds[i];
                UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == upgradeId);
                if (upgrade == null || !Save.regulationAffectedCategories.Contains(upgrade.category))
                {
                    continue;
                }

                // Removing the project entry too lets the upgrade be developed
                // again under the new regulations.
                Save.activeUpgradeProjects.RemoveAll(item => item.upgradeId == upgradeId);
                Save.completedUpgradeIds.RemoveAt(i);
                removed++;
            }

            string affected = string.Join(", ", Save.regulationAffectedCategories.ToArray());
            if (removed > 0)
            {
                Save.pendingRndMessages.Add("Regulation change hit " + affected + ": " + removed + " development project" + (removed == 1 ? "" : "s") + " scrapped for Season " + Save.currentSeason + ".");
                AddNews("Regulation shake-up wipes out " + removed + " development project" + (removed == 1 ? "" : "s") + " across the field.");
            }
            else
            {
                Save.pendingRndMessages.Add("Season " + Save.currentSeason + " regulation change in " + affected + " arrived - no completed projects affected.");
                AddNews("New season regulations target " + affected + " - teams begin adapting.");
            }

            PickRegulationTargets();
            Save.pendingRndMessages.Add("Next regulation focus: " + string.Join(", ", Save.regulationAffectedCategories.ToArray()) + " projects are at risk at season end.");
        }

        string PickRivalId(string teamId, string selectedDriverId)
        {
            List<DriverData> candidates = data.GetAiRaceDrivers(teamId, 8, selectedDriverId);
            if (candidates.Count == 0)
            {
                return "";
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].id != selectedDriverId)
                {
                    return candidates[i].id;
                }
            }

            return candidates[0].id;
        }

        int ContractTargetForTeam(string teamId)
        {
            TeamData team = data.FindTeam(teamId);
            if (team == null)
            {
                return 8;
            }

            if (team.reputation >= 90)
            {
                return 3;
            }

            if (team.reputation >= 82)
            {
                return 5;
            }

            if (team.reputation >= 74)
            {
                return 8;
            }

            return 10;
        }
    }
}
