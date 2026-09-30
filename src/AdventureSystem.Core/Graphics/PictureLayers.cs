using AdventureSystem.Core.Model;

namespace AdventureSystem.Core.Graphics;

/// <summary>A picture with others drawn over it (ShowPicture with N = 1), as one picture to render.</summary>
public static class PictureLayers
{
    /// <summary>
    /// <paramref name="basePicture"/> followed by the drawing commands of each layer, each starting from its own ink
    /// and paper as it would on its own. Returns the base picture itself when there are no layers.
    /// </summary>
    public static Picture Compose(Adventure adventure, Picture basePicture, IEnumerable<string>? layerIds)
    {
        var layers = (layerIds ?? Enumerable.Empty<string>()).Select(adventure.FindPicture).OfType<Picture>().ToList();
        if (layers.Count == 0) return basePicture;
        var options = Packaging.AdventurePackage.JsonOptions;
        var combined = System.Text.Json.JsonSerializer.Deserialize<Picture>(System.Text.Json.JsonSerializer.Serialize(basePicture, options), options)!;
        combined.Id = basePicture.Id + "+" + string.Join("+", layers.Select(l => l.Id));
        foreach (var layer in layers)
        {
            combined.Commands.Add(new DrawCommand { Op = DrawOp.SetInk, Color = layer.InitialInk });
            combined.Commands.Add(new DrawCommand { Op = DrawOp.SetPaper, Color = layer.InitialPaper });
            combined.Commands.AddRange(layer.Commands);
        }
        return combined;
    }

    /// <summary>The layer ids of a picture event (its Text), or none.</summary>
    public static string[] Parse(string? text) =>
        string.IsNullOrEmpty(text) ? Array.Empty<string>() : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
