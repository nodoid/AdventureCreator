# 6. Pictures and sound

## How pictures work

A picture is a list of **drawing commands** (lines, fills, shapes…) painted by the built-in renderer. It can also have a
bitmap background, or bitmap stamps, imported from an image file. Pictures are small (a few hundred bytes of commands)
and look identical on every platform. Parts of a picture can be **animated** (see *Animation* below).

A picture can be:
* a room's **Picture id** (shown when the player arrives and on LOOK)
* an item's **Picture id** (shown when examined)
* the game's **Intro Picture** or **Default Picture**
* shown at any time by the **ShowPicture** action

Pictures marked **Is Subroutine** are pieces drawn inside other pictures with the **Sub** tool, such as a tree used in several scenes.

### Picture properties

| Property | Meaning |
|---|---|
| Width, Height | Canvas size in pixels (for Smooth pictures, in drawing units). The default 256×176 is the classic ZX Spectrum picture size. |
| Render Mode | **Smooth**: drawn with Maui.Graphics as anti-aliased vectors, so lines, curves and text stay sharp at any size; the best choice for new pictures. **FullColour**: retro pixels, where every pixel has its own colour, scaled up with crisp edges. **SpectrumAttributes**: authentic ZX Spectrum rendering, where each 8×8 cell can hold only one ink and one paper colour, so colours "clash" (used by imported PAWS/Quill/GAC pictures). |
| Line Width | Smooth pictures only: the width of lines and outlines, in picture pixels (1 by default; the pen's brush size multiplies it). |
| Palette | The colours available to the drawing commands (32 by default: the 16 Spectrum colours plus 16 extra shades). Imported pictures use their machine's palette. |
| Initial Ink / Initial Paper | The starting drawing and background colours. |
| Bitmap Asset | An imported image drawn underneath the commands. |
| Is Subroutine | Only drawn when called from another picture. |

## The picture editor

Open **Pictures**, then select or add a picture. The editor is a WYSIWYG designer: the canvas shows the picture exactly as players will see it, and you work on the shapes directly.

* **Tool bar** (top): **↖ Select** plus the drawing tools. **↶ Undo / ↷ Redo** undo and redo changes to the picture. **# Grid** shows the 8×8 character cells.
* **Tool options** (under the tool bar): brush or text size, the fill pattern, or **Finish shape** while drawing a polygon. The coordinates under the pointer are shown on the right.
* **Canvas**: draw by dragging or clicking. A new shape is selected as soon as you draw it, so you can adjust it straight away.
* **Inspector** (right):
  * **Colour**: the palette. With nothing selected, the colour you click is the ink for new shapes. With a shape selected, clicking a colour **recolours that shape only**; the editor adds the ink changes needed so that nothing else changes colour. **Use as background** fills the whole picture with the ink (a Clear command at the start).
  * **Selection**: the selected shape's position and size (left, top, width, height; line ends; text and text size; pattern…). Type a value and press Enter. **Duplicate**, **To front**, **To back** and **Delete** act on the shape.
  * **Picture**: id, name, size, render mode, initial ink and paper, background image and sub-picture flag, plus **Background image…** and **Image to stamp…** (see *Using images*).
* **Drawing commands** (bottom): the picture as text, with one command per shape, drawn in order. Click the bar to **expand or collapse** it; the Studio remembers which you chose.
  * Selecting a command selects its shape on the canvas, and selecting a shape highlights its command.
  * **↑ Earlier / ↓ Later** reorder a command (later commands draw on top). **Delete** removes it. **Clear all** empties the picture.
  * **Step view** shows the picture only up to the selected command. Move the selection to watch the picture being drawn, which helps when a fill spills into the wrong area.

### Selecting and changing shapes

With **↖ Select**:
* click a shape to select it. Outlines are picked near their edges, filled shapes anywhere inside, and fills and plots near the point clicked. The topmost shape wins.
* drag a selected shape to move it
* drag its square **handles** to reshape it: line ends, rectangle and ellipse corners, polygon corners, or the bottom-right corner of an image
* click an empty area to clear the selection

Every change can be undone with **↶ Undo**.

| Tool | How to use it |
|---|---|
| ╱ Line | drag from start to end |
| ▭ Rect / ▬ Box | drag a rectangle (outline / filled) |
| ◯ Oval / ⬤ Disc | drag the bounding box of an ellipse (outline / filled) |
| ⬠ Poly / ⬟ Shape | click each corner; click the first corner again (or **Finish shape**) to close it (outline / filled) |
| ↖ Select | click a shape to select it, drag to move it, drag its handles to reshape it |
| ✎ Pen | drag freehand. **Brush** (in the tool options) sets the width. |
| · Plot | click to set one pixel |
| ▨ Fill | click inside an area to flood-fill it with the ink |
| ▦ Shade | flood-fill with a pattern (choose one of 7 patterns in the tool options). Ink is used where the pattern is set, paper elsewhere. |
| A Text | click where the text should start and type it. **Text size** sets the letter size. |
| ▣ Attr | drag over 8×8 cells to set their ink (Spectrum mode) |
| ⧉ Sub | click to place another picture here |
| 🖼 Image | drag a box to place an imported image (drag a tiny box for its natural size) |

**Drawing order matters.** Fill floods the area of the colour under the click: draw outlines first, then fill them. In Spectrum mode, Fill floods unset pixels up to the drawn lines, just like PAWS and the Illustrator.

### Using images

* **Background image…** stores a PNG, JPEG, HEIC, GIF… (converted to PNG, and scaled to at most 1024 pixels wide) as the picture's background. For a new, empty picture, the canvas takes the image's proportions. You can then draw on top of it.
* **Image to stamp…** stores an image that the **🖼 Image** tool can place anywhere, as often as you like.

Imported images are kept inside the game package (`assets/images/`). Large photographs make large games; prefer PNGs of a few hundred pixels.

### Smooth pictures and flood fills

Smooth pictures are drawn as shapes, not pixels, at whatever size the player's screen allows. Flood fills (▨ Fill and ▦ Shade) still work: the Studio traces each fill's area on a grid four times finer than the picture, with lines as wide as the smooth ones, so a fill meets its outline cleanly. For the smoothest results, use filled shapes (Box, Disc, Shape) where you can, and keep flood fills for areas bounded by several shapes.

Smooth pictures are drawn as pixels (full colour) where vectors can't be used, such as the exported apps' launch screens and the console player.

## Animation

Pictures can have simple looping animations: rain that falls, a lamp that flickers, a ship that bobs on the waves, lights that blink. An animation acts on a **layer**, meaning every shape with the same **Layer** name.

1. With **↖ Select**, click a shape and type a **Layer** name in the inspector (for example `rain`). Give every shape that should move together the same name.
2. In the inspector's **Animation** section, click **+ Add animation**, then choose its kind and layer.
3. Press **▶ Animate** (next to Undo) to preview. Press **■ Stop** to stop.

| Kind | What it does | Settings |
|---|---|---|
| **Blink** | Shows the layer for part of each period and hides it for the rest. | **Period**, **On %** |
| **Move** | Slides the layer by (Move X, Move Y) over the period, then back again, or jumps back to the start if **There and back** is off (good for falling rain). | **Period**, **Move X**, **Move Y**, **There and back** |
| **ColourCycle** | Steps the layer's colour through a list of palette colours, one per step (fire, flashing beacons, shimmering water). Type the colour numbers, or click a colour in the palette and press **+ ink**. | **Step**, **Colours** |
| **Flipbook** | Shows a different sub-picture on each step. Place a sub-picture with **⧉ Sub** in the layer, then list the frames' picture ids. | **Step**, **Frames** |

Times are in milliseconds. **Delay** holds an animation back at the start, and can make two animations run out of step. The tick box switches an animation off without deleting it. Several animations can act on the same layer; for example, a ship can bob with Move while its lights Blink.

Animations play in the Player and in the Studio's room preview and Test Play, in every render mode. Each one restarts when its picture is shown. They are purely visual and don't affect the game. *The Lighthouse* example uses all of these: rain on every outdoor scene, a pitching ship, a flickering fire in the cottage, a pulsing lamp beam and the ship's blinking lights.

## Sound

Open **Sounds** and click **+** to import an audio file. Each sound has an **Id**, a **Name** and a **Volume** (0–1).
**▶ Play** previews it; **Replace audio file…** swaps the file and keeps the id.

Ways to use sounds:

| Where | Effect |
|---|---|
| Room › Sound id (+ Loop Sound) | Ambient sound while the player is in the room. It continues seamlessly through rooms that share the same sound and stops on entering a room with no sound. |
| PlaySound action | A one-off effect (N = 0), or a new looping ambient track (N = 1). |
| StopSound action | Silence. |
| Game › Intro Sound id | Played when the game starts. |

**Formats:** WAV, MP3 and M4A/AAC play on all platforms. OGG does not play on Apple devices. Short effects are best as WAV; music and long ambience as MP3 or M4A.

Players can mute sound with **Sound › Mute** (⇧⌘M) on desktop.
