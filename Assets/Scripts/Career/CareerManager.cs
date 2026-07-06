using System.Collections.Generic;
using UnityEngine;

namespace LocalFormulaRacing
{
    public class CareerManager
    {
        const string CareerFile = "formula_racing_career.json";
        static readonly int[] Points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };
        const float UpgradeEffectScale = 2.25f;

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

            DriverData selected = data.FindDriver(selectedDriverId);
            if (useExistingDriver && selected != null)
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
                useExistingDriver = useExistingDriver,
                selectedDriverId = useExistingDriver && selected != null ? selected.id : "",
                rivalDriverId = PickRivalId(teamId, selectedDriverId),
                contractTargetPosition = ContractTargetForTeam(teamId),
                reputation = 25,
                resourcePoints = 500,
                difficultyIndex = 1,
                driverStandings = data.CreateInitialDriverStandings(driverName, teamId, useExistingDriver && selected != null ? selected.id : ""),
                constructorStandings = data.CreateInitialConstructorStandings()
            };
            if (useExistingDriver && selected != null)
            {
                Save.driverStandings.RemoveAll(entry => entry.id == selected.id);
            }
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
            }

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
            }

            Write();
        }

        public bool TryPurchaseUpgrade(string upgradeId)
        {
            UpgradeData upgrade = data.Upgrades.upgrades.Find(item => item.id == upgradeId);
            if (upgrade == null || Save.completedUpgradeIds.Contains(upgradeId) || Save.failedUpgradeIds.Contains(upgradeId))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(upgrade.requiredUpgradeId) && !Save.completedUpgradeIds.Contains(upgrade.requiredUpgradeId))
            {
                return false;
            }

            if (Save.resourcePoints < upgrade.cost)
            {
                return false;
            }

            Save.resourcePoints -= upgrade.cost;
            float roll = Random.value;
            if (roll <= upgrade.successChance)
            {
                Save.completedUpgradeIds.Add(upgradeId);
                Save.reputation += 1;
            }
            else
            {
                Save.failedUpgradeIds.Add(upgradeId);
            }

            Write();
            return true;
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

                tuned.topSpeed += Mathf.RoundToInt(upgrade.topSpeedDelta * 1.7f);
                tuned.acceleration += Mathf.RoundToInt(upgrade.accelerationDelta * UpgradeEffectScale);
                tuned.cornering += Mathf.RoundToInt(upgrade.corneringDelta * UpgradeEffectScale);
                tuned.braking += Mathf.RoundToInt(upgrade.brakingDelta * UpgradeEffectScale);
                tuned.reliability += Mathf.RoundToInt(upgrade.reliabilityDelta * 1.6f);
                tuned.ersEfficiency += Mathf.RoundToInt(upgrade.ersDelta * UpgradeEffectScale);
                tuned.tyreManagement += Mathf.RoundToInt(upgrade.tyreDelta * UpgradeEffectScale);
                tuned.aeroEfficiency += Mathf.RoundToInt(upgrade.aeroDelta * UpgradeEffectScale);
                tuned.chassisBalance += Mathf.RoundToInt(upgrade.chassisDelta * UpgradeEffectScale);
                tuned.enginePower += Mathf.RoundToInt(upgrade.engineDelta * UpgradeEffectScale);
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

            EnsurePlayerReplacesDriverSeat();
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
            if (Save.completedUpgradeIds.Count == 0)
            {
                return;
            }

            int removals = Mathf.Max(1, Mathf.RoundToInt(Save.completedUpgradeIds.Count * 0.25f));
            for (int i = 0; i < removals && Save.completedUpgradeIds.Count > 0; i++)
            {
                Save.completedUpgradeIds.RemoveAt(Random.Range(0, Save.completedUpgradeIds.Count));
            }
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
