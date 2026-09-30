namespace AdventureSystem.Core.Model;

/// <summary>How a non-player character moves around by itself.</summary>
public enum NpcMovement
{
    /// <summary>Stays put (unless moved by triggers or ordered to go somewhere).</summary>
    Stationary,
    /// <summary>Wanders through random exits (optionally limited to <see cref="NpcBehaviour.AllowedRooms"/>).</summary>
    Wander,
    /// <summary>Walks the <see cref="NpcBehaviour.Route"/> room by room, looping or turning back at the end.</summary>
    Patrol,
    /// <summary>Follows the player everywhere.</summary>
    Follow,
    /// <summary>Heads towards the player along the shortest route (hunters, pursuers).</summary>
    Seek,
    /// <summary>Moves away when the player is in the same room (shy or frightened creatures).</summary>
    Flee,
}

/// <summary>
/// Makes a character (an <see cref="Item"/> with <see cref="Item.IsCharacter"/>) a living part of the game: it can move
/// around, talk unprompted, react to the player, want things, steal, block the way, fight and obey orders.
/// All texts may use placeholders; <c>{npc}</c>/<c>{The npc}</c> name the character and <c>{direction}</c> the way it went.
/// </summary>
public sealed class NpcBehaviour
{
    /// <summary>Turns the behaviour off without deleting its settings (triggers can switch it on later).</summary>
    public bool Active { get; set; } = true;

    // ------------------------------------------------------------ movement
    public NpcMovement Movement { get; set; } = NpcMovement.Stationary;
    /// <summary>Percent chance of moving on a turn when it may move (100 = every time).</summary>
    public int MoveChance { get; set; } = 50;
    /// <summary>Only move every N turns (1 = any turn).</summary>
    public int MoveEvery { get; set; } = 1;
    /// <summary>Wander/Seek/Flee never leave these rooms (empty = anywhere).</summary>
    public List<string> AllowedRooms { get; set; } = new();
    /// <summary>Rooms visited in order by Patrol.</summary>
    public List<string> Route { get; set; } = new();
    /// <summary>Patrol: true = go back to the start after the last room; false = walk the route backwards.</summary>
    public bool RouteLoops { get; set; } = true;
    /// <summary>
    /// NPCs only ever move through real exits of the room they are in. Closed doors stop them unless this is on; locked
    /// doors always stop them; exits with conditions can be used only while the conditions are true.
    /// </summary>
    public bool OpensDoors { get; set; }
    /// <summary>Shown when the character leaves the player's room. Default: "{The npc} leaves, heading {direction}."</summary>
    public string DepartureMessage { get; set; } = "";
    /// <summary>Shown when the character enters the player's room. Default: "{The npc} arrives from the {direction}."</summary>
    public string ArrivalMessage { get; set; } = "";
    /// <summary>Shown when the character follows the player into a room. Default: "{The npc} follows you."</summary>
    public string FollowMessage { get; set; } = "";

    // ------------------------------------------------------------ talking and reacting
    /// <summary>Said the first time the player and the character meet.</summary>
    public string GreetingMessage { get; set; } = "";
    /// <summary>Random remarks or actions while in the player's room.</summary>
    public List<string> IdleMessages { get; set; } = new();
    /// <summary>Percent chance per turn of an idle message.</summary>
    public int IdleChance { get; set; } = 30;

    // ------------------------------------------------------------ giving
    /// <summary>Items the character wants. Giving one of them triggers <see cref="AcceptMessage"/> and <see cref="OnAccept"/>.</summary>
    public List<string> Wants { get; set; } = new();
    public string AcceptMessage { get; set; } = "";
    public List<GameAction> OnAccept { get; set; } = new();
    /// <summary>Response to being offered anything else. Default: "{The npc} doesn't want {the noun1}."</summary>
    public string RefuseMessage { get; set; } = "";
    /// <summary>After receiving a wanted item the character starts following the player.</summary>
    public bool FollowsWhenGiven { get; set; }
    /// <summary>After receiving a wanted item the character stops blocking exits and stops being hostile.</summary>
    public bool PacifiedWhenGiven { get; set; }

    // ------------------------------------------------------------ getting in the way
    /// <summary>Exit directions the character blocks while it is in the room ("*" = every exit).</summary>
    public List<string> BlocksExits { get; set; } = new();
    /// <summary>Default: "{The npc} blocks your way."</summary>
    public string BlockMessage { get; set; } = "";
    /// <summary>Percent chance per turn of stealing something the player carries (while in the same room).</summary>
    public int StealChance { get; set; }
    /// <summary>Only these items are stolen (empty = anything carried but not worn).</summary>
    public List<string> StealsItems { get; set; } = new();
    /// <summary>Default: "{The npc} snatches {the noun1} from you!"</summary>
    public string StealMessage { get; set; } = "";
    /// <summary>Picks up portable items it finds lying around (a magpie, a tidy butler).</summary>
    public bool CollectsItems { get; set; }
    /// <summary>Only these items are collected (empty = anything portable).</summary>
    public List<string> CollectsOnly { get; set; } = new();

    // ------------------------------------------------------------ combat
    /// <summary>Attacks the player when in the same room.</summary>
    public bool Hostile { get; set; }
    /// <summary>Becomes hostile when attacked.</summary>
    public bool RetaliatesWhenAttacked { get; set; }
    /// <summary>Percent chance per turn of attacking when hostile.</summary>
    public int AttackChance { get; set; } = 50;
    /// <summary>Damage to the player's health per hit (see <see cref="GameSettings.PlayerHealth"/>).</summary>
    public int Damage { get; set; } = 1;
    /// <summary>Default: "{The npc} attacks you!"</summary>
    public string AttackMessage { get; set; } = "";
    /// <summary>Printed when the player dies from the character's attacks. Default: "{The npc} has killed you."</summary>
    public string KillMessage { get; set; } = "";
    /// <summary>Hit points. 0 = cannot be hurt (attacking gives the usual "violence isn't the answer").</summary>
    public int Health { get; set; }
    /// <summary>Default: "You hit {the npc}."</summary>
    public string HitMessage { get; set; } = "";
    /// <summary>Default: "{The npc} collapses."</summary>
    public string DefeatMessage { get; set; } = "";
    /// <summary>Removed from the game when defeated (it runs away, vanishes…); otherwise it stays, inactive.</summary>
    public bool RemoveWhenDefeated { get; set; }
    public List<GameAction> OnDefeat { get; set; } = new();

    // ------------------------------------------------------------ orders
    /// <summary>Carries out simple orders itself: go, follow/stay, take, drop, give, open, close, wait.</summary>
    public bool ObeysOrders { get; set; }
    /// <summary>Default: "{The npc} does as you ask."</summary>
    public string ObeyMessage { get; set; } = "";
}

/// <summary>Changeable NPC state saved with the game.</summary>
public sealed class NpcState
{
    public int Health { get; set; }
    public bool Defeated { get; set; }
    public bool Following { get; set; }
    public bool? Hostile { get; set; }
    public bool? Blocking { get; set; }
    public bool? Active { get; set; }
    public NpcMovement? Movement { get; set; }
    public int RouteIndex { get; set; }
    public int RouteStep { get; set; } = 1;
    public bool Greeted { get; set; }
    /// <summary>Direction the NPC last left the player's room by (so the player can FOLLOW it).</summary>
    public string? LastExit { get; set; }
    public string? LastExitRoom { get; set; }
    public HashSet<string> Received { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Room the NPC is walking to (set by NpcGoTo), or null.</summary>
    public string? Destination { get; set; }
}
