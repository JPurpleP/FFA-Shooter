# FFA Shooter

Online free-for-all first-person shooter with three maps of the same size (about 136 × 112 m). **Fluketown** is a
Nuketown-style suburb: the original cul-de-sac block plus a north avenue, two side streets, eight more houses, a south
park, playground, court and water tower. **Timberline** is a summer camp in a forested valley: mess hall, four cabins,
campfire, tents, a pond with a dock, a parking lot and gate, and wooded hills along the west and east edges (fire
lookout tower on the west ridge, ranger station and deer stand on the east one) under ~1,300 trees. **Downtown** is a
city grid (three streets × three avenues, 16 blocks): walk-in brick shops and apartments with stairs to the roofs, office
and hotel tower lobbies, a two-level parking garage with ramps, a construction site with a crane, a warehouse with a
mezzanine, a gas station, a fountain plaza and a basketball court, ringed by a skyline. Unity 6000.6, Built-in render pipeline,
Netcode for GameObjects 2.13 + Unity Transport (host/client, online through Unity Relay join codes). Everything visual (map, weapons,
bodies, gore, sounds) is generated from code — no imported art or audio.

## Play
- Run `Builds/Windows/FFA-Shooter.exe`. Main menu: **Play** (name, Host with a map choice, Join —
  joiners load whatever map the host picked),
  **Customize** (skins with a live 3D preview: Original, Rainbow, Hi-Vis Worker, Tactical, Chef, Clown,
  Banana Suit, Astronaut, Cowboy, Pirate, Alien, Luchador, Tourist,
  Confederate Soldier, Redneck, Propeller Kid,
  Office Dad, Suburban Dad, Hitman, Street OG, Disco King, Ninja, Viking, Knight, Mime, Skater, Farmer, Lumberjack, Surgeon,
  Firefighter, Hazmat, WW2 German Soldier — 32 in all), **Settings** (sensitivity,
  field of view, volume, gore level). Choices are saved between launches; your skin syncs to everyone online.
- **Online play.** Hosting goes through Unity Relay: the host gets a 6-letter join code (shown in the lobby, the Esc menu
  with a copy button, the HUD corner and the Tab leaderboard), friends type it into Join. No port forwarding, no IP
  addresses shared. There is no LAN / direct-IP option in the menu (direct connections remain only as command-line test flags).
- **Lobby.** Hosting or joining puts you in a waiting lobby (player list, join code). Everyone presses **Ready Up**; the
  host's **Start Match** unlocks once every player is ready. No bots in normal games (the F1 dev panel can still add them).
  Players who join mid-match drop straight in.
- **Spawn protection:** for 3 s after spawning you can't be hurt (and can't be pulled into a golden duel). Moving, jumping,
  firing, hooking or throwing ends it early. A "PROTECTED" tag shows over protected enemies in your line of sight.
- **Killcam:** when someone kills you, the last ~3 seconds are replayed through their eyes (with their gun), with you
  walking your last steps, before you respawn.
- First to 25 kills wins; after 8 seconds everyone goes back to the lobby to ready up for the next match. Hold Tab for the leaderboard (rank, kills, deaths, K/D, progress to 25).
- Controls: WASD · Shift sprint · Space jump · LMB fire · RMB aim (scope on the Barrett) · **Q/E lean** · R reload ·
  F pick up / swap · 1/2/3 or mouse wheel to switch (3 = throwables; press 3 again for the next type) · LMB with slot 3 out or **hold T** from any slot, release to throw · Tab scores · Esc menu (resume, sensitivity / FOV / volume, join code, leave)
- Leaning moves your head (and its hitbox) out from cover; it stops short of walls.
- **Ctrl**: tap (or press while standing still / walking) to crouch or stand. **Hold Ctrl while sprinting to slide** —
  a burst of speed that decays; keep holding to end in a crouch, Space to slide-jump. **Hold Ctrl in the air** and you
  land straight into a slide that keeps all your speed (chain slide-jumps, or land a grapple swing / rocket jump sliding). **Z** goes prone / stands up.
  Crouch and prone are slower but more accurate and harder to hit; Space stands you up (not under a low ceiling).
- **G / middle mouse: grappling hook** — once you've picked one up. Hooks are their own pickups (6 per map, on spots between
  the weapon spawns, back 45 s after being taken); you keep it until you die, then it drops where you fell. (30 m, 2.5 s cooldown.) Reels you in fast; G or Space releases and you keep the
  momentum, so you can swing and launch. Arriving at a ledge pops you up onto it. Cyan brackets = hookable target.

## Killstreaks
Kills since your last death count as a streak (shown bottom-left, with the next reward). Each reward lasts 30 s.
- **3 in a row: Speed hack.** Double running, walking and sliding speed.
- **5 in a row: ESP.** Every enemy is outlined through walls: box, name, distance, health bar and their live skeleton.
- **8 in a row: Flyhack.** No gravity: Space rises, Ctrl sinks (kept under 45 m and inside the map). When it ends, you fall.
- **10 in a row: Aimbot.** Your aim snaps to the head of the visible enemy nearest your crosshair.
Dying resets the streak (running rewards finish their 30 s); a new match clears everything. Bots earn them too (perfect
aim / hunting you through walls / speed; bots don't fly).

## Weapons
| Weapon | Mag | Notes |
|---|---|---|
| Glock 17 | 17 (+17 spare) | everyone spawns with it |
| MP5 | 30 (+90) | full auto, ghost-ring diopter |
| M4A1 | 30 (+90) | full auto, round Aimpoint-style red dot |
| PPSh-41 | 71-round drum (+142) | very fast full auto, iron sights |
| M249 SAW | 100-round box (+100) | belt-fed, slow to move and reload, iron sights |
| Remington 870 | 6 (+18) | 9 pellets, shell-by-shell reload, ghost-ring sights |
| AA-12 | 20-round drum (+40) | full-auto 12 gauge, red dot |
| Mosin-Nagant | 5 (+20) | bolt action, iron sights, one-shot headshot |
| Barrett M82A1 | 10 (+30) | scoped, one-shot body |
| M32 MGL | 6 (+6) | 40mm grenades in an arc, red dot |
| RPG-7 | 1 (+3) | fast rocket, big blast, backblast |
| Frag grenade | start with 2, max 4 | slot 3; cook while held (3.2 s fuse), release to throw; bounces |
| Smoke grenade | max 2 | slot 3; a thick cloud for ~16 s that also blinds bots and the aimbot |
| Rope grenade | max 2 | slot 3; after 1.3 s it tethers everyone in sight within 15 m to where it landed for 10 s: they move freely but can't get any farther from it than they were |
| Molotov | max 2 | slot 3; shatters on impact into a 3.6 m pool of fire for 8 s (24 damage/s, credited to the thrower) |
| M1911 | 7 (+21) | .45 ACP, tall sights |
| Glock (switch) | 33 (+66) | full-auto, held sideways one-handed (sights still line up) |
| Cattleman | 6 (+24) | single-action .45 Colt revolver, loads one round at a time |
| .44 Magnum | 6 (+24) | double-action revolver, hard hitting |
| Lever Action | 7 (+28) | .30-30, lever cycles after each shot, open sights |
| KRISS Vector | 25 (+75) | .45 ACP, very fast full auto, Super V keeps recoil low, red dot |
| MP-18 | 1 (+15) | single-shot break-action .308, hard hitting; the barrel tips open and you load each round by hand |
| Golden Deagle | 7 (+7) | **doesn't hurt — starts a golden duel** (below) | **The most common pickup: twice the odds of any other weapon.** |

Aiming down sights puts the exact point every shot goes (screen centre) on the tip of the front post / bead, the centre
of the red dot, or the scope crosshair; sights are open notches, ghost rings or optics so the target stays in view.
Check it with `-ffacenterdot` (magenta dot at the true centre).
Recoil follows the cartridge: 9mm / 5.56 / 7.62×25 guns barely move, the .45 ACP kicks a little, and magnums, .50s, rifle
rounds and 12 gauge kick hard (the biggest also jolt the view).

**Pickups are completely random.** Every map has ~32 spawn spots spread evenly over ground you can walk to (streets,
yards, ground floors, trails — not roofs), picked automatically when the map is built. Each roll is any weapon from the
table (not the Glock); every weapon has an equal share except the golden Deagle, which has a double share, and a new roll every time it respawns (60 s after being taken).

**Golden duel.** Hit someone with the golden Deagle and you're both pulled into a boxing ring in an underground fight
club, at full health, fists only (LMB punches, hold to keep swinging; head punches hit harder). Fight to the death: after
45 s sudden death makes you both bleed out. The winner goes back to exactly where they stood when the shot landed, at full
health, with all their guns; the loser dies, their guns drop on the map where they were, and they respawn as usual. Up to
four duels can run at once. Bots fight duels too (and go for the golden gun).

Two weapon slots plus slot 3 for throwables. Picking up a gun you already carry takes its ammo; with both slots full you swap your current gun.
**Grenades spawn on their own spots** (10 per map, a random throwable each time, back 40 s after being taken), separate from the gun spawns.
Dead players drop everything they carry (despawns after 30 s, 60 s for the Barrett).

## Gore
Kills spawn a ragdoll thrown according to the weapon's power. `GorePlan` (seeded by the server) decides dismemberment so every
client sees the same thing. Outcomes: limbs torn off at either joint, heads exploded or torn off whole, torsos blown open,
bodies cut in half, and total dismemberment ("OBLITERATED": every joint fails and the pieces are blown outward). Blood spray,
red mist, wall/floor splatter rings, spreading pools, gibs and bone, guts, gushing stumps, screen blood. `Gore.Multiplier`
(default 2x, F1 slider up to 4x) scales blood and gib volume.

**For explosives:** send a kill with `DeathInfo.Explosive = true` and `ExplosionCenter` set — it always obliterates the
body away from the blast. Call `Gore.Explosion(center, radius, speed)` on every client to tear apart and throw corpses
already lying nearby. Gore lab (F1) has a "Blast" hit option and a "Test blast at crosshair" button.

## Testing tools
- **F1 dev panel** (host; only in the editor, development builds, or with `-ffadev`): spawn bots (Dummy / Strafer / Turret / Fighter), bot accuracy, give weapons, god mode, infinite ammo,
  **gore lab** (spawn a dummy in front of you and execute it with any weapon/body part, or auto-cycle), gore multiplier,
  slow-motion, damage numbers, bot status labels, bot nav-graph overlay, respawn pickups, clear corpses.
- Command line:
  ```
  -ffamap <0-2>    host Fluketown / Timberline / Downtown (not saved)
  -ffastreak <n>   start with an n-kill streak   -ffaturn <deg>  swing the view   -ffahook  start with the grappling hook
  -ffabackground [-ffareturnfocus <hwnd>]   test window that never takes focus or the mouse (pair with -monitor 2)
  -ffahost | -ffarelay | -ffaclient <ip or join code>   (command-line hosts skip the lobby unless -ffalobby;
                    -ffaready readies a client up, -ffastartwhenready makes the host start once all are ready)
  -ffaname <n>  -ffaport <p>  -ffaquitafter <s>  -ffafraglimit <n>  shorter matches (tests)
  -ffaprofile <name>   separate Unity services sign-in (several online copies on one PC)
  -ffabots <n> [Dummy|Strafer|Turret|Fighter]   spawn bots after hosting
  -ffaautotest     the local player is driven by the Fighter bot AI (logs scores)
  -ffagive <0-4>   start with a weapon    -ffaaim  hold aim-down-sights    -ffalean <-1..1>  hold a lean
  -ffagrapple <pitch>   look up by <pitch> degrees and fire the hook
  -ffagoretest     auto-cycling gore lab   -ffagorepart <0-5>  -ffagoreweapon <0-5>
  -ffaat <x> <z> <yaw>   teleport   -ffadummy <dist> [-ffadummyyaw <deg>] [-ffadummypose 0-3]   -ffastance 1|2|3|4|5 (crouch, prone, slide, air slide, fly up)   -ffathrowtype 0-3  throw a frag / smoke / rope / molotov with -ffathrow   -ffagive 22-24  give smoke / rope / molotov   -ffadev  enable the F1 panel   -ffaduel  start a golden duel with the first bot
  -ffashots <prefix> <count> <interval>   screenshot series   -ffascreenshot <file>
  -ffamenutab <0-2>  -ffaskin <id>  -ffaskinline [first] [count]   -ffafire   hold the trigger   -ffathrow  cook and throw a grenade   -ffabotgive <id>  arm all bots        -ffatopdown [size] [x] [z]   orthographic map overview, UI hidden
  ```
- Headless match: `FFA-Shooter.exe -batchmode -nographics -ffahost -ffaautotest -ffabots 5 Fighter -ffaquitafter 60 -logFile host.log`
  (`[FFA] Kill:` lines show weapon, headshots and gore outcome).

## Project layout
- `Assets/Scripts/FpsPlayer.cs` – movement/aim/inventory (owner), shooting/damage/pickups/respawn (server)
- `Assets/Scripts/Weapons/` – stats (`WeaponDefs`), procedural models (`WeaponModel`), pickups and spawners
- `Assets/Scripts/Bodies/BodyModel.cs` – primitive humanoid with arm IK and walk cycle; hit-location classification
- `Assets/Scripts/Effects/` – ragdolls and gore (`Gore`, `GorePlan`), procedural sound (`Sfx`)
- `Assets/Scripts/Bots/` – `BotBrain` (bot behaviours), `BotNav` (auto-generated waypoint graph + A*)
- `Assets/Scripts/Dev/` – dev panel and settings
- `Assets/Scripts/MapCatalog.cs` – every map lives in the one scene under its own root; switches the active map,
  its lighting and the bot graph (clients follow the host via `FpsPlayer.MapId`)
- `Assets/Editor/ProjectBuilder.cs` – generates textures, materials, prefabs and Fluketown; `ProjectBuilder.Forest.cs`
  builds Timberline (heightmap ground mesh, generated tree/rock meshes, cabins, lookout); `ProjectBuilder.City.cs`
  builds Downtown (metre-scaled facade/brick meshes via `metricTile`, walk-in shops, garage, construction site). Re-run **FFA > Rebuild Arena**
  after changing it (overwrites `Assets/Generated` and the scene). Build: `-executeMethod ProjectBuilder.BuildWindows`.

## Online setup (one time)
1. Open the project in Unity 6000.6.5f1 and sign in with your Unity account.
2. **Edit > Project Settings > Services**: pick your organization and **Create project ID** (or link an existing one).
3. At [cloud.unity.com](https://cloud.unity.com), open that project and enable **Relay** (it has a free tier).
4. Rebuild. Until this is done, hosting shows "Online play isn't set up yet".
- Code: `Assets/Scripts/RelayConnect.cs` (anonymous sign-in, allocation, join codes, error messages); `GameUI.Host/Join`.
