using AdventureSystem.Core.Model;

namespace AdventureSystem.Importers.Common;

/// <summary>
/// Numbered choices for choice-based stories (Twine, Quest gamebooks): each choice is a hidden exit reached by
/// typing its number, and the room description lists the choices.
/// </summary>
internal static class Choices
{
    public const int Max = 20;

    public static string Direction(int n) => $"choice{n}";

    /// <summary>Adds choice <paramref name="n"/> to <paramref name="room"/>: "<paramref name="n"/>. <paramref name="text"/>".</summary>
    public static void Add(Adventure a, Room room, int n, string targetRoomId, string text)
    {
        if (n > Max) return;
        room.Exits.Add(new Exit { Direction = Direction(n), TargetRoomId = targetRoomId, Hidden = true });
        room.Description = (room.Description.TrimEnd() + $"\n{n}. {text.Trim()}").TrimStart('\n');
        a.Vocabulary.Directions[n.ToString()] = Direction(n);
        a.Vocabulary.Directions[Direction(n)] = Direction(n);
    }

    /// <summary>Settings suited to choice-based stories.</summary>
    public static void Configure(Adventure a)
    {
        a.Settings.AutoListExits = false;
        a.Settings.Prompt = "Choose a number: ";
        a.Messages["CantGo"] = "Type the number of one of the choices.";
        a.Messages["DontUnderstand"] = "Type the number of one of the choices.";
        a.Messages["UnknownWord"] = "Type the number of one of the choices.";
    }
}
