# 10. NPCs, random events, traps and flooding

This chapter covers everything that happens *around* the player:
* **non-player characters (NPCs)** who move, talk, want things, steal, block the way, fight and obey orders
* **random events** that can happen anywhere, even out of sight
* **traps**, **flooding**, and **player health**

---

## Non-player characters

Any character (an item with **Is Character** on) can be given an **NPC behaviour**. In **Items & People**, select the
character and click **+ Add NPC behaviour** at the bottom of the editor. The Test Play **Watch** panel shows every
NPC's location and state (following, hostile, defeated, walking to…) after each move.

### Movement — only through real exits

NPCs obey the same map as the player: **an NPC can only leave a room through one of that room's exits.**

| Situation | What the NPC can do |
|---|---|
| A room with no exits | Stays there forever, unless a trigger moves it with MoveItem. |
| A closed door | Blocks the NPC, unless **Opens Doors** is on (it then opens the door, and the player sees this if they are there). |
| A locked door | Always blocks the NPC. |
| An exit with conditions | Usable only while the conditions are true. |
| A flooded room | NPCs never enter it. |

| Movement | Behaviour |
|---|---|
| **Stationary** | Stays put, unless ordered, sent with NpcGoTo, or made to follow. |
| **Wander** | Takes a random usable exit. **Move Chance** % of the time; every **Move Every** turns. **Allowed Rooms** keeps it within an area. |
| **Patrol** | Walks the **Route** room by room. At the end it goes back to the start (**Route Loops**) or walks the route backwards. Between route rooms it takes the shortest path through exits; a route room it can't reach means it waits. |
| **Follow** | Follows the player. Each time the player moves, the NPC comes along if there is an exit between the two rooms. If the player teleports or takes a one-way exit, the NPC catches up later along the shortest path. |
| **Seek** | Hunts the player along the shortest path (pursuers, monsters). |
| **Flee** | Moves away through a random exit when the player is in the same room (shy creatures). |

When an NPC leaves or enters the player's room, the player sees:
* **Departure Message**: default "{The npc} leaves, heading {direction}."
* **Arrival Message**: default "{The npc} arrives from the {direction}."

The player can then type **FOLLOW** (the NPC's name) to go the same way.

The **NpcGoTo** action sends an NPC walking to a room. It moves one room per turn through exits and gives up if there is no route.

### Talking and reacting

| Setting | Effect |
|---|---|
| **Greeting Message** | Said the first time the player and the NPC meet. |
| **Idle Messages** | Random remarks or actions (one per line) while in the player's room, **Idle Chance** % per turn. |
| **Topics** (on the item itself) | ASK / TELL / TALK TO conversations, as for any character ([chapter 3](03-world-reference.md#conversation-topics)). |
| **NpcSay** action | Makes the NPC say something if the player is there. |

### Wanting things

List item ids in **Wants**. When the player gives the NPC one of those items:
* the NPC keeps it
* **Accept Message** is printed
* the **On Accept** actions run
* the **ItemGiven** trigger event fires

Two shortcuts make gifts change the NPC's behaviour:
* **Follows When Given**: the NPC starts following the player (a dog fed a biscuit).
* **Pacified When Given**: it stops blocking exits and stops being hostile (a troll paid in gold).

Anything else offered gets the **Refuse Message**.

### Getting in the way

| Setting | Effect |
|---|---|
| **Blocks Exits** | Directions it guards while in the room (`*` = all). The player gets the **Block Message** instead of moving. |
| **Steal Chance**, **Steals Items** | Each turn in the player's room, it may snatch a carried item (only the listed items, if any). |
| **Collects Items**, **Collects Only** | Picks up things lying in whatever room it is in (a magpie, a tidy butler). |

Items an NPC holds are shown in room descriptions ("a magpie (carrying a ring)") and can be examined, but the player can't take them.
* *Ask the NPC for* an item works only if it isn't hostile, a thief or a collector.
* Defeating an NPC makes it drop everything it holds.

### Combat and health

Combat needs **Game › Settings › Player Health**. It is 0 by default, which means no health system: attacks print their message but do no harm.

| Setting | Effect |
|---|---|
| **Hostile** | Attacks the player each turn they share a room (**Attack Chance** %), dealing **Damage**. |
| **Retaliates When Attacked** | Becomes hostile when the player attacks it. |
| **Health** | The NPC's hit points. 0 = cannot be hurt (ATTACK gives the usual "Violence isn't the answer"). |
| **Hit / Defeat / Kill / Attack Messages** | Texts for each moment of a fight. |
| **Remove When Defeated**, **On Defeat** | Remove it from the game (it runs away), and actions to run. |

When the player types *attack the goblin* or *hit the goblin with the sword*:
* The damage is 1 plus the weapon's **Damage** (an item property).
* If no weapon is named, the best weapon they carry is used.
* At 0 health the NPC is defeated and drops its possessions, and the **NpcDefeated** trigger fires.

When the player's health falls to a quarter or less they're told they are badly hurt. At 0 they die: the attacker's **Kill Message**, or **Settings › Death Message**. Players can type **DIAGNOSE** (or HEALTH) to check. When health is on, the status bar shows it.

### Orders

With **Obeys Orders** on, the NPC carries out simple orders itself:
* *Robot, go north*, *tell the robot to go east*: only through a real exit of its room, of course.
* *Robot, follow me* / *robot, wait*.
* *Robot, take the box* / *drop it* / *give me the key* / *open the hatch* / *close it*.

Hostile NPCs ignore orders. For anything else, write a BeforeCommand trigger with **Subject** = the NPC ([chapter 4](04-triggers.md#the-pattern)).

### Changing NPCs during play

| Action | Use |
|---|---|
| **SetNpc** | A = NPC, B = `Movement` (Text = Stationary/Wander/Patrol/Follow/Seek/Flee), `Hostile`, `Following`, `Blocking`, `Active` (N = 1/0) or `Health` (N). |
| **NpcGoTo** | Send the NPC walking to a room. |
| **MoveItem** | Teleport it (ignores exits: use it for magic, cut-scenes, or bringing it into the game). |
| **NpcSay** | Make it speak. |

Conditions:
* **NpcFollowing**, **NpcHostile**, **NpcDefeated**
* **NpcIn** (A = NPC, B = room)
* **NpcHasItem** (A = NPC, B = item)
* **HealthAtLeast**

Trigger events:
* **NpcArrives** and **NpcLeaves** (in the player's room)
* **NpcDefeated**
* **ItemGiven**
* **PlayerHurt**

For all of these, Subject = the NPC.

### Example: The Lighthouse

* **Skipper**, Tom's dog, wanders the cottage, path and beach. Give him the dog biscuit (on the cottage table) and he follows you, even up the lighthouse stairs.
* A **herring gull** wanders the beach and path, and tries to steal the conch shell. *Shoo the gull* (an attack with Health 1 and friendly messages) sends it flying off, dropping what it took.

---

## Random events

Open **Random Events** in the sidebar. Each turn, every enabled event rolls its chance; if it happens, its actions run.

| Setting | Meaning |
|---|---|
| **Chance** / **Chance Per Thousand** | Probability per turn: percent plus thousandths (Chance 0 + 5‰ = a rare 0.5%). |
| **Where** | **Anywhere** (any room, or one of **Rooms**), **PlayerRoom**, **AwayFromPlayer** (happens out of sight), **Global** (no particular room: weather, power cuts). |
| **Rooms** | Candidate rooms (empty = all). One room that meets the conditions is picked at random. |
| **Earliest Turn** / **Latest Turn** | When it may happen (Latest 0 = no limit). |
| **Cooldown** | Minimum turns between occurrences. |
| **Max Times** | How often it can ever happen (0 = unlimited). |
| **Conditions** | Checked for each candidate room, where `@eventroom` means that room. |
| **Actions** | Anything a trigger can do. Inside an event, `@eventroom` is the chosen room and `$randomitem` a random portable item there. **CreateItem** puts items in `@eventroom` by default. |
| **Witness Message** | Shown when the player is in the event's room (always, for Global events). |
| **Distant Message** | Shown when it happens elsewhere ("You hear a crash from somewhere above."). Leave it empty for things the player simply discovers later. |

Other actions and conditions:
* **RunRandomEvent** makes an event happen now. Its chance and cooldown are ignored, but its conditions still apply.
* **EventHappened** tests whether an event has happened at least once.

### Ideas and how to build them

| Event | Settings | Actions |
|---|---|---|
| A thief rearranges things | Where AwayFromPlayer, Chance 5 | MoveItem A `$randomitem`, B `@randomroom` |
| Supplies wash up | Rooms `beach`, Chance 8, Max Times 3 | CreateItem A `driftwood` |
| Power cut | Global, Chance 3, Cooldown 20 | SetDark A `@here` N 1, then a second event with condition IsDark to restore it |
| Lamp blown out | Where PlayerRoom, condition ItemCarried `lamp` | SetLit A `lamp` N 0, Message "A gust snuffs your lamp!" |
| Cave-in | Rooms `mine1\|mine2\|mine3`, Where AwayFromPlayer, Max Times 1 | SetExit A `@eventroom` B `north` Text *(empty)*; Distant Message "A deep rumble shakes the ground." |
| Flash flood | Rooms `cellar`, Chance 10 | Flood A `@eventroom` N 1; a second event (condition RoomFlooded `@eventroom`) with Flood N 0 to drain it later |
| Booby trap | Where AwayFromPlayer, Chance 5 | SetTrap A `@eventroom` N 2, Text "A crossbow bolt whistles past – and grazes you!", B "You spot a thin tripwire." |
| Wandering monster appears | Global, Chance 4, Max Times 1, condition ItemExists `troll` (tick **not**) | MoveItem A `troll` B `@randomroom`, SetNpc A `troll` B `Movement` Text `Seek` |

---

## Traps

**SetTrap** (A = room, N = damage or −1 for deadly, Text = what happens when it springs, B = what the player sees on finding it) places a trap. Triggers or random events can set one anywhere. **ClearTrap** removes it.

1. **Entering** a trapped room gives the player a warning: "You have an uneasy feeling about this place."
2. On their **next turn**, anything other than a careful action springs the trap. It then does its damage, or kills if deadly, and is gone. Careful actions are: SEARCH, LOOK, EXAMINE, LOOK UNDER, LISTEN, SMELL, WAIT, INVENTORY or DISARM.
3. **SEARCH** (with no object) finds the trap. A found trap is harmless: the player steps around it. **DISARM** removes it for good.

The condition **RoomTrapped** tests whether a room has a trap. Without Player Health, a trap with damage just prints its message; deadly traps always kill.

## Flooding

**Flood** (A = room, N = 1 to flood, 0 to drain) or **SetRoomFlag** with the flag `flooded`:

* The player **can't enter** a flooded room ("… is flooded. You can't go there without a boat or diving gear") unless they carry or wear an item with **Allows Water** on (a boat, a raft, a diving suit).
* NPCs never enter flooded rooms.
* A flooded room's description says so, and light sources lying in it go out.
* If the player's own room floods and they have nothing that allows water, they are **swept out** through an exit to a dry room. With no way out, they drown.
* The condition **RoomFlooded** tests for it.

## Room flags

**SetRoomFlag** (A = room, B = any flag name, N = 1/0) and the **RoomHasFlag** condition give rooms your own states: `onfire`, `smoky`, `searched`, `haunted`… Combine them with triggers, for example EnterRoom + RoomHasFlag `smoky` → HurtPlayer 1 "You choke on the smoke."

## Placeholders

These are added to the list in [chapter 4](04-triggers.md#text-placeholders):

| Placeholder | Becomes |
|---|---|
| `{npc}`, `{The npc}`, `{a npc}` | The NPC concerned (in NPC messages and NPC triggers). |
| `{health}`, `{maxhealth}` | The player's health. |
| `{eventroom}` | The name of the random event's room. |

Special values:
* item fields: `$npc`, `$randomitem`, `$randomcarried`
* room fields: `@here`, `@eventroom`, `@randomroom`
* variable: `@health`
