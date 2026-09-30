using System.IO.Compression;
using AdventureSystem.Core.Engine;
using AdventureSystem.Core.Model;
using AdventureSystem.Importers.Common;
using AdventureSystem.Importers.Quest;

namespace AdventureSystem.Tests.Importers;

public class QuestImporterTests
{
    private const string Game = """
<asl version="580">
  <include ref="English.aslx" />
  <include ref="Core.aslx" />
  <game name="The Test House">
    <author>Tester</author>
    <start type="script">
      msg ("Welcome!")
      game.coins = 0
    </start>
  </game>
  <object name="hall">
    <inherit name="editor_room" />
    <alias>Great Hall</alias>
    <description>A grand hall.</description>
    <picture>hall.png</picture>
    <object name="player">
      <inherit name="editor_object" />
      <inherit name="editor_player" />
    </object>
    <object name="lamp">
      <inherit name="editor_object" />
      <take />
      <look>A brass lamp.</look>
      <alt type="stringlist"><value>lantern</value></alt>
      <rub type="script">
        if (Got(lamp)) {
          msg ("A genie appears!")
          IncreaseScore (5)
          lamp.rubbed = true
        }
        else {
          msg ("You need to be holding it.")
        }
      </rub>
    </object>
    <object name="chest">
      <inherit name="editor_object" />
      <inherit name="container_closed" />
    </object>
    <exit alias="north" to="vault" name="vaultdoor">
      <inherit name="northdirection" />
      <locked />
      <lockmessage>The vault door is locked.</lockmessage>
    </exit>
  </object>
  <object name="vault">
    <inherit name="editor_room" />
    <description type="script">
      s = "The vault is cold. "
      if (lamp.rubbed) {
        s = s + "Gold glitters everywhere."
      }
      msg (s)
    </description>
    <exit alias="south" to="hall">
      <inherit name="southdirection" />
    </exit>
  </object>
  <verb>
    <property>rub</property>
    <pattern>rub;polish</pattern>
    <defaultexpression>"Rubbing " + object.article + " does nothing."</defaultexpression>
  </verb>
  <command name="opendoor">
    <pattern>say open sesame</pattern>
    <script>
      if (lamp.rubbed and player.parent = hall) {
        msg ("The vault door swings open.")
        UnlockExit (vaultdoor)
      }
      else {
        firsttime {
          msg ("Nothing happens.")
        }
        otherwise {
          msg ("Still nothing.")
        }
      }
    </script>
  </command>
</asl>
""";

    private static byte[] Aslx() => System.Text.Encoding.UTF8.GetBytes(Game);

    private static byte[] Package()
    {
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAIAAAA2iEnWAAAAEElEQVR4nGP4z8DAwMDAAAAr/wL+2vT1YQAAAABJRU5ErkJggg==");
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var s = zip.CreateEntry("game.aslx").Open()) s.Write(Aslx());
            using (var s = zip.CreateEntry("hall.png").Open()) s.Write(png);
        }
        return ms.ToArray();
    }

    [Fact]
    public void ConvertsTheWorld()
    {
        var data = Package();
        var importer = ImporterRegistry.Detect(data, "house.quest");
        Assert.IsType<QuestImporter>(importer);
        var result = importer!.Import(data, "house.quest");
        var a = result.Adventure;
        Assert.Equal("The Test House", a.Title);
        Assert.Equal("Tester", a.Author);
        Assert.Equal("hall", a.StartRoomId);
        Assert.Equal("Great Hall", a.FindRoom("hall")!.Name);
        var lamp = a.FindItem("lamp")!;
        Assert.True(lamp.Portable);
        Assert.Contains("lantern", lamp.Nouns);
        Assert.Equal("A brass lamp.", lamp.Description);
        var chest = a.FindItem("chest")!;
        Assert.True(chest.Container && chest.Openable && !chest.IsOpen);
        // The room picture came from the package.
        var picture = a.FindPicture(a.FindRoom("hall")!.PictureId);
        Assert.NotNull(picture);
        Assert.Equal((2, 3), (picture!.Width, picture.Height));
        Assert.Equal(PictureRenderMode.Smooth, picture.RenderMode);
    }

    [Fact]
    public void ScriptsBecomeWorkingTriggers()
    {
        var a = new QuestImporter().Import(Aslx(), "house.aslx").Adventure;
        var e = new GameEngine(a, randomSeed: 1);
        Assert.Contains("Welcome!", e.Start().Text);

        Assert.Contains("You need to be holding it.", e.Submit("rub lamp").Text);        // else branch
        Assert.Contains("The vault door is locked.", e.Submit("n").Text);                 // locked exit
        Assert.Contains("Nothing happens.", e.Submit("say open sesame").Text);            // firsttime
        Assert.Contains("Still nothing.", e.Submit("say open sesame").Text);              // otherwise

        e.Submit("take lamp");
        var rub = e.Submit("polish lantern").Text;                                         // verb synonym, item synonym
        Assert.Contains("A genie appears!", rub);
        Assert.DoesNotContain("You need to be holding it.", rub);                         // only one branch runs
        Assert.Equal(5, e.State.Score);

        Assert.Contains("The vault door swings open.", e.Submit("say open sesame").Text);
        var vault = e.Submit("n").Text;
        Assert.Equal("vault", e.State.CurrentRoomId);
        Assert.Contains("The vault is cold. Gold glitters everywhere.", vault.Replace("\n", ""));   // text built in a local variable
    }
}
