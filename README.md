# Local Formula Racing Prototype

Private, local-only Unity prototype for a Formula-style simcade career/race vertical slice. It uses original placeholder visuals generated at runtime from Unity primitives, editable JSON text data, and local JSON saves only.

## Open And Run On macOS

1. Open Unity Hub.
2. Add this folder as a project: `/Users/jerryhuang/Documents/F1 game`.
3. Open with Unity 2022.3 LTS or a newer compatible Unity 3D editor.
4. Open `Assets/Scenes/Boot.unity`.
5. Press Play.

The boot scene is intentionally empty. `GameBootstrap` creates the menus, camera, lights, procedural track, vehicles, HUD, race manager, and results flow at runtime.

## Folder Structure

- `Assets/Scripts/Core`: boot, settings, audio, shared helpers.
- `Assets/Scripts/Vehicle`: player and AI vehicle physics, tyres, damage, input, camera.
- `Assets/Scripts/AI`: racing-line driver logic.
- `Assets/Scripts/Race`: procedural track, checkpoints, lap timing, race order, results.
- `Assets/Scripts/Career`: career save, standings, R&D upgrade handling.
- `Assets/Scripts/UI`: runtime-created menus, HUD, pause, results screens.
- `Assets/Scripts/Data`: serializable data models and JSON repository.
- `Assets/Resources/Data`: editable JSON for teams, drivers, calendar, car performance, upgrades.
- `Assets/Scenes`: boot scene.
- `Assets/Prefabs`, `Assets/Materials`, `Assets/Audio`, `Assets/Tracks`, `Assets/Cars`: reserved project folders for future authored assets.

## Controls

- Throttle: `W` or `Up Arrow`; gamepad `Vertical` axis up when available.
- Brake / reverse: `S` or `Down Arrow`; gamepad `Vertical` axis down when available.
- Steer left / right: `A` / `D` or `Left Arrow` / `Right Arrow`; gamepad `Horizontal` axis when available.
- ERS mode cycle: `R` cycles Balanced, Deploy/Attack, and Harvest.
- ERS manual override: `Left Shift` or `Right Shift`.
- DRS: `Space` toggles open/closed when race rules allow it. It closes automatically when unavailable or outside a DRS zone.
- Camera toggle: `C`.
- Pause / resume: `Esc`.
- Pit request: `P`.
- Manual shift down / up: `Q` / `E`, only when manual gears are enabled.
- Restart race: open pause with `Esc`, then choose `Restart Race`.
- Return to menu: open pause with `Esc`, then choose `Main Menu`.
- Assists: auto-brake, ABS, traction control, racing line, ERS mode, manual gears, and input sensitivity are changed from `Settings` / `Assists`. ERS mode can also be cycled in-race with `R`.

## Current Vertical Slice

- Main menu, career entry, quick race, settings panel.
- Career hub, driver ratings table, race weekend screen, qualifying results, race results, assists menu, and pause/restart/return flow.
- Twenty-four race weekends route into real-world-inspired procedural layout templates, including distinct added layouts for China-style technical, Miami-style stadium, Canada-style stop-go, Spain-style flowing, Austria-style hillside, Hungary-style technical, Netherlands-style coastal, Madrid-style hybrid street, Azerbaijan-style fast street, United States-style rollercoaster, Mexico-style stadium, Las Vegas-style night street, and Qatar-style high-speed rounds.
- Player open-wheel placeholder car with original primitive livery parts, halo, sidepods, wings, airbox, diffuser, driver helmet, and team colors.
- Full 22-driver race weekends using real driver/team names as editable text labels. The player occupies one grid seat, with 21 AI cars completing the field.
- Weekend flow from menu to qualifying to race to results. Career race weekends always begin with qualifying before the race grid is available.
- Q1/Q2/Q3-style qualifying classification with slowest-driver elimination, player out lap, simulated AI phase times, and saved grid order for career race starts.
- Race starts with countdown, jump-start penalties, lap timing, sectors, position order, standings, constructor points.
- Local career save and settings save in `Application.persistentDataPath`.
- Tyre compounds, tyre temperature performance windows, wear, mandatory dry-race pit rule for longer races, pit service hold, simplified damage, ERS strategy, DRS zones/rules, fuel burn, off-track slowdown, kerb vibration, track-limit warnings and penalties.
- R&D upgrade tree data with first- and second-stage upgrades, resource points, reputation, rival, and contract target.
- Driving assists: auto-brake, ABS, traction control, racing line, steering/throttle/brake sensitivity.

## Tuning Difficulty And Assists

- `Settings > Difficulty` changes AI pace, braking margin, mistake rate, qualifying speed, and race aggression.
- The race weekend grid is fixed at 22 drivers: player plus 21 AI cars.
- `Settings > Race Laps` cycles short, 5-lap, and longer prototype race distances.
- `Settings > Tyre` picks the player's starting dry compound unless weather requires intermediate/wet tyres.
- `Settings > ERS Mode` chooses Balanced, Attack/Deploy, or Harvest behavior for player ERS assist logic. `R` cycles the same mode during a race.
- `Settings > Assists` toggles auto-brake, ABS, traction control, racing-line rendering, and input sensitivity.
- Car/team performance is editable in `Assets/Resources/Data/carPerformance.json`.
- Driver pace, qualifying, aggression, defending, overtaking, consistency, and tyre management are editable in `Assets/Resources/Data/drivers.json`.
- Driver overall rating is calculated from four visible 1-100 categories: qualifying, defending, overtaking, and race pace. View them from `Driver Ratings`.

## Session And Strategy Rules

- Qualifying includes an out lap before timed laps. The out lap does not count as a timed lap; the timer for the push lap starts when the car crosses start/finish after the out lap.
- If the first qualifying push lap is invalidated, the run continues automatically into the second push lap.
- Q1 has 22 cars and eliminates the slowest 6 for P17-P22. Q2 has 16 cars and eliminates the slowest 6 for P11-P16. Q3 has 10 cars and decides P1-P10.
- If the player fails to set a valid time in Q1, Q2, or Q3, they are classified last in that specific segment.
- AI qualifying times use driver qualifying pace, race pace, consistency, tyre management, wet skill, car performance, weather, phase pressure, rare mistakes, and a best-of-two-run improvement model.
- Tyre temperature affects grip, braking, traction, and wear. Cold tyres brake and turn worse; optimal tyres are fastest; overheated tyres slide more and wear faster.
- DRS is available only in marked zones after race start. In races it is disabled for the first two completed laps and then requires roughly a 1.0 second live interval to the car ahead. In qualifying it is free in DRS zones.
- ERS drains while deployed and regenerates most under hard braking, with a small coasting recharge. Battery is clamped from 0-100%.

## Known Limitations

- Practice is still not a full playable session.
- Procedural tracks are inspired layout archetypes, not accurate replicas.
- Pit entry/exit pathing is simplified to a pit service zone and timed hold.
- AI can race, qualify, defend, attack, pit, and make mistakes, but close-quarters avoidance remains prototype-level.
- Audio is generated with simple runtime clips and placeholder tones.
- No official logos, liveries, sponsor marks, faces, helmets, copied broadcast art, or copyrighted audio are included.

## Next Improvement Checklist

- Build practice programs and one-shot/short/full qualifying variants.
- Expand AI side-by-side collision avoidance and pit-lane pathing.
- Add remappable control UI and modern Unity Input System support.
- Add weather transitions and wet tyre strategy.
- Turn the R&D panel into a scrollable visual upgrade tree.
- Replace primitive cars with original low-poly models.
- Add replay/spectator camera controls and post-race highlights.
