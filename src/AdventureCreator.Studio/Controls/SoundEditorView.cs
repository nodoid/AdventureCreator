using AdventureCreator.Core.Audio;
using AdventureCreator.Core.Model;
using AdventureCreator.Maui;
using Theme = AdventureCreator.Maui.Theme;

namespace AdventureCreator.Studio.Controls;

/// <summary>
/// The Studio's sound editor. Generated sound effects are designed with presets and sliders (sfxr style) and can be
/// changed at any time; WAV sounds, generated or imported, can be edited on their waveform: trim, delete, fades,
/// normalise, volume, reverse and echo, with undo.
/// </summary>
public sealed class SoundEditorView : ContentView
{
    private readonly SoundAsset sound;
    private readonly EditorContext ctx;
    private readonly AudioService audio;

    private float[] samples = Array.Empty<float>();
    private int sampleRate = SfxSynth.SampleRate;
    private string? notEditable;
    private int selectionStart, selectionEnd;   // samples; equal = no selection

    private readonly GraphicsView waveform;
    private readonly Label subtitle = new() { FontSize = 12, TextColor = Theme.SecondaryText };
    private readonly Label timeInfo = new() { FontSize = 12, TextColor = Theme.SecondaryText };
    private readonly Label status = new() { FontSize = 12, TextColor = Theme.Accent };
    private readonly VerticalStackLayout designer = new() { Spacing = 10 };
    private readonly VerticalStackLayout editTools = new() { Spacing = 6 };
    private readonly CheckBox autoPlay = new() { IsChecked = true, VerticalOptions = LayoutOptions.Center };
    private IDispatcherTimer? regenerateTimer;

    private readonly List<(byte[] Wav, SoundEffect? Effect)> undo = new(), redo = new();
    /// <summary>The effect settings as last stored (sliders change <see cref="SoundAsset.Effect"/> before it is re-rendered).</summary>
    private SoundEffect? storedEffect;

    public SoundEditorView(SoundAsset sound, EditorContext ctx, AudioService audio)
    {
        this.sound = sound;
        this.ctx = ctx;
        this.audio = audio;

        var title = new Label { Text = string.IsNullOrWhiteSpace(sound.Name) ? sound.Id : sound.Name, FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Theme.Accent };

        waveform = new GraphicsView { Drawable = new WaveDrawable(this), HeightRequest = 150, BackgroundColor = Theme.Pane };
        waveform.StartInteraction += (_, e) => { selectionStart = selectionEnd = SampleAt(e.Touches[0].X); waveform.Invalidate(); };
        waveform.DragInteraction += (_, e) => { selectionEnd = SampleAt(e.Touches[0].X); UpdateTimeInfo(); waveform.Invalidate(); };
        waveform.EndInteraction += (_, e) =>
        {
            if (e.Touches.Length > 0) selectionEnd = SampleAt(e.Touches[0].X);
            if (Math.Abs(selectionEnd - selectionStart) < sampleRate / 200) selectionStart = selectionEnd = 0;   // a click clears
            UpdateTimeInfo();
            waveform.Invalidate();
        };
        var waveFrame = new Border
        {
            Content = waveform, Stroke = Theme.Border, StrokeThickness = 1, Padding = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        };
        ToolTipProperties.SetText(waveFrame, "Drag across the waveform to select part of the sound; click to clear the selection");

        var play = Button("▶ Play", "Play the whole sound", () => Play(selectionOnly: false));
        var playSelection = Button("▶ Selection", "Play the selected part", () => Play(selectionOnly: true));
        var stop = Button("■ Stop", "Stop playing", () => audio.StopAll());
        var replace = Button("Replace with audio file…", "Import a WAV, MP3 or M4A file in place of this sound", async () => await ReplaceAsync());
        var makeEffect = Button("Make it a sound effect", "Replace this sound with a generated effect you can design", () => StartEffect("Pickup"));
        makeEffect.IsVisible = sound.Effect == null;

        Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new VerticalStackLayout { Spacing = 2, Children = { title, subtitle } },
                new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { play, playSelection, stop, replace, makeEffect } },
                waveFrame,
                timeInfo,
                status,
                PlaybackSettings(),
                designer,
                editTools,
                new SectionView("Sound", new ObjectEditor(sound, ctx, only: new[] { "Id", "Name" })),
                new Label
                {
                    Text = "Play sounds with the PlaySound action, or set a room's Sound for ambient audio. Generated effects and edited sounds are stored as WAV. " +
                           "MP3 and M4A files play everywhere but can't be edited here; OGG isn't supported on Apple platforms.",
                    FontSize = 12, TextColor = Theme.SecondaryText,
                },
            },
        };

        storedEffect = sound.Effect?.Clone();
        Unloaded += (_, _) => regenerateTimer?.Stop();
        LoadSamples();
        BuildDesigner();
        BuildEditTools();
    }

    // =================================================================== loading and saving

    private byte[]? Bytes => ctx.Adventure.Assets.TryGetValue(sound.AssetName, out var b) ? b : null;

    private void LoadSamples()
    {
        notEditable = null;
        var bytes = Bytes;
        if (bytes == null) { samples = Array.Empty<float>(); notEditable = "The audio file is missing."; }
        else if (!WavFile.IsWav(bytes)) { samples = Array.Empty<float>(); notEditable = $"{Path.GetExtension(sound.AssetName).TrimStart('.').ToUpperInvariant()} sounds can be played but not edited. Use a WAV file, or make it a sound effect, to edit it."; }
        else
        {
            try { (samples, sampleRate) = WavFile.Decode(bytes); }
            catch (InvalidDataException ex) { samples = Array.Empty<float>(); notEditable = ex.Message; }
        }
        selectionStart = selectionEnd = 0;
        UpdateSubtitle();
        UpdateTimeInfo();
        if (cutOffRow.Children.Count > 0) UpdateCutOff();
        waveform.Invalidate();
    }

    private void UpdateSubtitle()
    {
        var bytes = Bytes;
        string kind = sound.Effect != null ? $"Sound effect{(string.IsNullOrEmpty(sound.Effect.Preset) ? "" : " (" + sound.Effect.Preset + ")")}"
            : WavFile.IsWav(bytes ?? Array.Empty<byte>()) ? "WAV recording" : Path.GetExtension(sound.AssetName).TrimStart('.').ToUpperInvariant() + " file";
        string length = samples.Length > 0 ? $" · {(double)samples.Length / sampleRate:0.00} s" : "";
        subtitle.Text = $"{kind}{length} · {(bytes == null ? "missing" : $"{bytes.Length / 1024.0:0.#} KB")}";
    }

    private void UpdateTimeInfo()
    {
        if (notEditable != null) { timeInfo.Text = notEditable; return; }
        var (from, to) = Selection();
        timeInfo.Text = HasSelection
            ? $"Selection {Seconds(from)} – {Seconds(to)} ({Seconds(to - from)}). Edits apply to the selection."
            : $"Length {Seconds(samples.Length)}. Drag across the waveform to select part of it; edits apply to the whole sound otherwise.";
    }

    private string Seconds(int n) => $"{(double)n / Math.Max(1, sampleRate):0.00} s";

    /// <summary>Stores new audio for the sound (and optionally its effect settings), keeping an undo step.</summary>
    private void Store(byte[] wav, SoundEffect? effect, bool keepUndo = true)
    {
        if (keepUndo)
        {
            if (Bytes is { } old) undo.Add((old, storedEffect?.Clone()));
            if (undo.Count > 50) undo.RemoveAt(0);
            redo.Clear();
        }
        if (!sound.AssetName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var old = sound.AssetName;
            sound.AssetName = UniqueAssetName();
            if (!ctx.Adventure.Sounds.Any(s => s != sound && s.AssetName == old)) ctx.Adventure.Assets.Remove(old);
        }
        ctx.Adventure.Assets[sound.AssetName] = wav;
        sound.Effect = effect;
        storedEffect = effect?.Clone();
        ctx.Changed(sound);
        LoadSamples();
        BuildEditTools();
    }

    private string UniqueAssetName()
    {
        var name = $"sounds/{sound.Id}.wav";
        for (int i = 2; ctx.Adventure.Assets.ContainsKey(name) && !ctx.Adventure.Sounds.Any(s => s == sound && s.AssetName == name); i++) name = $"sounds/{sound.Id}_{i}.wav";
        return name;
    }

    // =================================================================== playing

    private void Play(bool selectionOnly)
    {
        if (Bytes is not { } bytes) return;
        if (selectionOnly && HasSelection && notEditable == null)
        {
            var (from, to) = Selection();
            bytes = WavFile.Encode(samples[from..to], sampleRate);
        }
        if (selectionOnly && HasSelection) audio.Preview(bytes, sound.Volume);
        else audio.Preview(bytes, sound.Volume, sound.Repeat, sound.CutOffSeconds);
    }

    // =================================================================== playback settings

    private readonly Grid cutOffRow = new() { ColumnDefinitions = { new(new GridLength(90)), new(GridLength.Star), new(new GridLength(70)) }, ColumnSpacing = 8 };
    private readonly Slider cutOffSlider = new() { Minimum = 0, Maximum = 10, VerticalOptions = LayoutOptions.Center };
    private readonly Label cutOffValue = new() { FontSize = 12, TextColor = Theme.SecondaryText, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };

    /// <summary>Volume (0–10), Repeat, and a cut-off time that is greyed out while Repeat is on.</summary>
    private View PlaybackSettings()
    {
        Grid Row(string name, View control, View? value = null)
        {
            var g = new Grid { ColumnDefinitions = { new(new GridLength(90)), new(GridLength.Star), new(new GridLength(70)) }, ColumnSpacing = 8 };
            g.Add(new Label { Text = name, FontSize = 13, TextColor = Theme.Text, VerticalOptions = LayoutOptions.Center }, 0);
            g.Add(control, 1);
            if (value != null) g.Add(value, 2);
            return g;
        }

        // Volume: 0–10 on screen, 0–1 in the game file.
        var volumeValue = new Label { FontSize = 12, TextColor = Theme.SecondaryText, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };
        var volume = new Slider { Minimum = 0, Maximum = 10, Value = Math.Round(sound.Volume * 10, 1), VerticalOptions = LayoutOptions.Center };
        volumeValue.Text = $"{volume.Value:0.#}";
        volume.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue * 2) / 2;           // steps of 0.5
            volumeValue.Text = $"{v:0.#}";
            if (Math.Abs(sound.Volume - v / 10) < 1e-9) return;
            sound.Volume = v / 10;
            ctx.Changed(sound);
        };
        volume.DragCompleted += (_, _) => Play(false);
        var volumeRow = Row("Volume", volume, volumeValue);
        ToolTipProperties.SetText(volumeRow, "How loud the sound plays in the game, from 0 (silent) to 10 (full)");

        var repeat = new Switch { IsToggled = sound.Repeat, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Start };
        repeat.Toggled += (_, e) =>
        {
            sound.Repeat = e.Value;
            ctx.Changed(sound);
            UpdateCutOff();
        };
        var repeatRow = Row("Repeat", repeat, new Label { Text = "", FontSize = 12 });
        ToolTipProperties.SetText(repeatRow, "Play on a loop until it's stopped (the StopSound action, or leaving the room whose sound it is)");

        cutOffSlider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue, 1);
            if (Math.Abs(sound.CutOffSeconds - v) < 1e-9) return;
            sound.CutOffSeconds = v;
            ctx.Changed(sound);
            UpdateCutOff();
        };
        cutOffSlider.DragCompleted += (_, _) => Play(false);
        cutOffRow.Add(new Label { Text = "Cut off after", FontSize = 13, TextColor = Theme.Text, VerticalOptions = LayoutOptions.Center }, 0);
        cutOffRow.Add(cutOffSlider, 1);
        cutOffRow.Add(cutOffValue, 2);
        ToolTipProperties.SetText(cutOffRow, "Stop the sound after this time (Off = play to the end). Not used while Repeat is on.");
        UpdateCutOff();

        return new SectionView("Playback", new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                volumeRow, repeatRow, cutOffRow,
                new Label { Text = "These apply wherever the sound plays: PlaySound actions, room sounds and the preview above.", FontSize = 11, TextColor = Theme.SecondaryText },
            },
        });
    }

    private void UpdateCutOff()
    {
        // The slider covers the sound's own length (at least a second); longer cut-offs would never apply.
        double length = samples.Length > 0 ? (double)samples.Length / sampleRate : 10;
        cutOffSlider.Maximum = Math.Max(1, Math.Ceiling(length * 10) / 10);
        cutOffSlider.Value = Math.Min(sound.CutOffSeconds, cutOffSlider.Maximum);
        cutOffValue.Text = sound.CutOffSeconds <= 0 ? "Off" : $"{sound.CutOffSeconds:0.0} s";
        // Greyed out (and not editable) while Repeat is on.
        cutOffRow.IsEnabled = !sound.Repeat;
        cutOffRow.Opacity = sound.Repeat ? 0.4 : 1;
        waveform.Invalidate();
    }

    // =================================================================== effect designer

    private void StartEffect(string preset, int? seed = null)
    {
        var fx = SfxPresets.Make(preset, seed);
        Store(SfxSynth.RenderWav(fx), fx);
        BuildDesigner();
        if (autoPlay.IsChecked) Play(false);
    }

    private void BuildDesigner()
    {
        designer.Children.Clear();
        var fx = sound.Effect;
        if (fx == null) return;

        designer.Children.Add(Heading("Sound effect designer"));
        var presets = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (name, icon) in SfxPresets.Names)
        {
            var chip = new Chip($"{icon} {name}", $"Start again from the {name} preset");
            chip.IsSelected = fx.Preset == name;
            chip.Clicked += (_, _) => StartEffect(name);
            presets.Children.Add(chip);
        }
        designer.Children.Add(presets);

        var randomise = new Chip("🎲 Random " + (string.IsNullOrEmpty(fx.Preset) ? "sound" : fx.Preset.ToLowerInvariant()), "A random variation of this preset");
        randomise.Clicked += (_, _) => StartEffect(string.IsNullOrEmpty(fx.Preset) ? "Pickup" : fx.Preset, Random.Shared.Next(1, 1_000_000));
        var mutate = new Chip("≈ Mutate", "Change every setting slightly");
        mutate.Clicked += (_, _) =>
        {
            var changed = SfxPresets.Mutate(sound.Effect!, Random.Shared.Next(1, 1_000_000));
            Store(SfxSynth.RenderWav(changed), changed);
            BuildDesigner();
            if (autoPlay.IsChecked) Play(false);
        };
        designer.Children.Add(new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Children = { randomise, mutate, autoPlay, new Label { Text = "Play after each change", FontSize = 12, TextColor = Theme.Text, VerticalOptions = LayoutOptions.Center } },
        });

        var cards = new List<View>();
        cards.Add(Card("Tone",
            WavePicker(fx),
            Param("Pitch", "Hz", 20, 4000, () => fx.Frequency, v => fx.Frequency = v, "Starting pitch"),
            Param("Slide to", "Hz", 0, 4000, () => fx.SlideTo, v => fx.SlideTo = v, "Pitch at the end (0 = no slide)"),
            Param("Duty", "%", 5, 95, () => fx.Duty, v => fx.Duty = v, "Square wave shape: 50 % is hollow, low values are thin and buzzy"),
            Param("Vibrato", "st", 0, 12, () => fx.VibratoDepth, v => fx.VibratoDepth = v, "Vibrato depth in semitones", 1),
            Param("Speed", "Hz", 0.5, 40, () => fx.VibratoSpeed, v => fx.VibratoSpeed = v, "Vibrato speed", 1)));
        cards.Add(Card("Pitch jump and repeat",
            Param("Jump", "st", -24, 24, () => fx.JumpSemitones, v => fx.JumpSemitones = v, "Jump the pitch up (or down) by this many semitones"),
            Param("Jump at", "ms", 0, 1000, () => fx.JumpAtMs, v => fx.JumpAtMs = v, "When the jump happens (0 = no jump)"),
            Param("Repeat", "ms", 0, 1000, () => fx.RepeatMs, v => fx.RepeatMs = v, "Restart the slide and jump every so often (0 = off): alarms, trills")));
        cards.Add(Card("Envelope",
            Param("Attack", "ms", 0, 2000, () => fx.AttackMs, v => fx.AttackMs = v, "Time to fade in"),
            Param("Sustain", "ms", 0, 4000, () => fx.SustainMs, v => fx.SustainMs = v, "Time at full volume"),
            Param("Decay", "ms", 0, 4000, () => fx.DecayMs, v => fx.DecayMs = v, "Time to die away"),
            Param("Punch", "%", 0, 100, () => fx.Punch, v => fx.Punch = v, "A burst of extra volume at the start")));
        cards.Add(Card("Filters and effects",
            Param("Low-pass", "%", 1, 100, () => fx.LowPass, v => fx.LowPass = v, "Lower values sound muffled (100 = off)"),
            Param("High-pass", "%", 0, 100, () => fx.HighPass, v => fx.HighPass = v, "Higher values sound thin (0 = off)"),
            Param("Crush", "%", 0, 100, () => fx.Crush, v => fx.Crush = v, "Retro bit-crush: fewer bits, lower sample rate"),
            Param("Echo", "ms", 0, 1000, () => fx.EchoMs, v => fx.EchoMs = v, "Echo delay (0 = off)"),
            Param("Feedback", "%", 0, 90, () => fx.EchoFeedback, v => fx.EchoFeedback = v, "How much each echo repeats")));
        cards.Add(Card("Output",
            Param("Volume", "%", 0, 100, () => fx.Volume, v => fx.Volume = v, "Loudness of the effect"),
            new Label { Text = $"Length {fx.LengthMs / 1000:0.00} s plus any echo.", FontSize = 11, TextColor = Theme.SecondaryText }));
        designer.Children.Add(CardGrid(cards));
    }

    /// <summary>
    /// Lays the cards out two to a row when there's room, otherwise one per row. (A wrapping FlexLayout clipped the
    /// taller cards on macOS.)
    /// </summary>
    private static View CardGrid(List<View> cards)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        int columns = 0;
        void Arrange(int count)
        {
            if (count == columns) return;
            columns = count;
            grid.Children.Clear();
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (int c = 0; c < count; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int r = 0; r < (cards.Count + count - 1) / count; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < cards.Count; i++) grid.Add(cards[i], i % count, i / count);
        }
        Arrange(2);
        grid.SizeChanged += (_, _) => { if (grid.Width > 0) Arrange(grid.Width >= 640 ? 2 : 1); };
        return grid;
    }

    private View WavePicker(SoundEffect fx)
    {
        var picker = new Picker { ItemsSource = Enum.GetNames<SoundWave>(), SelectedItem = fx.Wave.ToString(), FontSize = 13 };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedItem is string w && Enum.TryParse<SoundWave>(w, out var wave) && wave != fx.Wave)
            {
                fx.Wave = wave;
                EffectChanged(playNow: true);
            }
        };
        var row = new Grid { ColumnDefinitions = { new(new GridLength(70)), new(GridLength.Star) }, ColumnSpacing = 6 };
        row.Add(new Label { Text = "Wave", FontSize = 12, TextColor = Theme.Text, VerticalOptions = LayoutOptions.Center }, 0);
        row.Add(picker, 1);
        ToolTipProperties.SetText(row, "Square: retro beeps. Sawtooth: buzzy. Triangle and Sine: soft. Noise: explosions, hits, wind.");
        return row;
    }

    private View Param(string name, string unit, double min, double max, Func<double> get, Action<double> set, string tip, int decimals = 0)
    {
        var value = new Label { FontSize = 12, TextColor = Theme.SecondaryText, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };
        string Format(double v) => $"{Math.Round(v, decimals)} {unit}";
        value.Text = Format(get());
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(get(), min, max), VerticalOptions = LayoutOptions.Center };
        slider.ValueChanged += (_, e) =>
        {
            var v = Math.Round(e.NewValue, decimals);
            if (Math.Abs(v - get()) < 1e-9) return;
            set(v);
            value.Text = Format(v);
            EffectChanged(playNow: false);
        };
        slider.DragCompleted += (_, _) => { if (autoPlay.IsChecked) Play(false); };
        var row = new Grid { ColumnDefinitions = { new(new GridLength(70)), new(GridLength.Star), new(new GridLength(64)) }, ColumnSpacing = 6 };
        row.Add(new Label { Text = name, FontSize = 12, TextColor = Theme.Text, VerticalOptions = LayoutOptions.Center }, 0);
        row.Add(slider, 1);
        row.Add(value, 2);
        ToolTipProperties.SetText(row, tip);
        return row;
    }

    /// <summary>Re-renders the effect shortly after the last change, so dragging a slider stays smooth.</summary>
    private void EffectChanged(bool playNow)
    {
        regenerateTimer ??= CreateRegenerateTimer();
        regenerateTimer.Stop();
        regenerateTimer.Start();
        pendingPlay |= playNow;
    }

    private bool pendingPlay;
    private bool effectSnapshotTaken;

    private IDispatcherTimer CreateRegenerateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(80);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (sound.Effect is not { } fx) return;
            // One undo step per burst of slider changes.
            Store(SfxSynth.RenderWav(fx), fx, keepUndo: !effectSnapshotTaken);
            effectSnapshotTaken = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(600), () => effectSnapshotTaken = false);
            if (pendingPlay && autoPlay.IsChecked) Play(false);
            pendingPlay = false;
        };
        return timer;
    }

    // =================================================================== waveform editing

    private void BuildEditTools()
    {
        editTools.Children.Clear();
        editTools.Children.Add(Heading("Edit the waveform"));
        if (notEditable != null)
        {
            editTools.Children.Add(new Label { Text = notEditable, FontSize = 12, TextColor = Theme.SecondaryText });
            return;
        }
        var tools = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        void Tool(string icon, string name, string tip, Func<float[], float[]?> edit)
        {
            var chip = new Chip($"{icon} {name}", tip);
            chip.Clicked += (_, _) => ApplyEdit(name, edit);
            tools.Children.Add(chip);
        }
        Tool("✂", "Trim to selection", "Keep only the selected part", s => HasSelection ? SoundEditing.Trim(s, Selection().From, Selection().To) : null);
        Tool("⌫", "Delete selection", "Remove the selected part", s => HasSelection ? SoundEditing.Delete(s, Selection().From, Selection().To) : null);
        Tool("◢", "Fade in", "Fade in over the selection (or the first quarter)", s => { var (f, t) = RangeOr(0.25, start: true); SoundEditing.FadeIn(s, f, t); return s; });
        Tool("◣", "Fade out", "Fade out over the selection (or the last quarter)", s => { var (f, t) = RangeOr(0.25, start: false); SoundEditing.FadeOut(s, f, t); return s; });
        Tool("⇕", "Normalise", "Make the loudest point full volume", s => { SoundEditing.Normalize(s, selectionStart, selectionEnd); return s; });
        Tool("🔊", "Louder", "3 dB louder", s => { SoundEditing.Gain(s, selectionStart, selectionEnd, 1.4125f); return s; });
        Tool("🔉", "Quieter", "3 dB quieter", s => { SoundEditing.Gain(s, selectionStart, selectionEnd, 0.7079f); return s; });
        Tool("⇄", "Reverse", "Play backwards", s => { SoundEditing.Reverse(s, selectionStart, selectionEnd); return s; });
        Tool("〰", "Echo", "Add an echo (200 ms, 40 %)", s => SoundEditing.Echo(s, sampleRate, 0.2, 0.4));
        editTools.Children.Add(tools);

        var undoChip = new Chip("↶ Undo", "Undo the last change to this sound") { Opacity = undo.Count > 0 ? 1 : 0.45 };
        undoChip.Clicked += (_, _) => UndoRedo(undo, redo);
        var redoChip = new Chip("↷ Redo", "Redo") { Opacity = redo.Count > 0 ? 1 : 0.45 };
        redoChip.Clicked += (_, _) => UndoRedo(redo, undo);
        editTools.Children.Add(new HorizontalStackLayout { Children = { undoChip, redoChip } });
        if (sound.Effect != null)
            editTools.Children.Add(new Label
            {
                Text = "Editing the waveform turns the effect into a plain recording, so its designer settings no longer apply. Undo brings them back.",
                FontSize = 11, TextColor = Theme.SecondaryText,
            });
    }

    private void ApplyEdit(string name, Func<float[], float[]?> edit)
    {
        if (samples.Length == 0) return;
        var result = edit(samples.ToArray());
        if (result == null) { status.Text = "Select part of the waveform first."; return; }
        bool wasEffect = sound.Effect != null;
        Store(WavFile.Encode(result, sampleRate), null);
        if (wasEffect) BuildDesigner();
        status.Text = $"{name}: done." + (wasEffect ? " The sound is now a recording; Undo brings the effect back." : "");
    }

    private void UndoRedo(List<(byte[] Wav, SoundEffect? Effect)> from, List<(byte[] Wav, SoundEffect? Effect)> to)
    {
        if (from.Count == 0 || Bytes is not { } current) return;
        to.Add((current, storedEffect?.Clone()));
        var (wav, effect) = from[^1];
        from.RemoveAt(from.Count - 1);
        Store(wav, effect, keepUndo: false);
        BuildDesigner();
        status.Text = "";
    }

    private bool HasSelection => Math.Abs(selectionEnd - selectionStart) > 0;

    private (int From, int To) Selection() => SoundEditing.Range(samples, Math.Min(selectionStart, selectionEnd), Math.Max(selectionStart, selectionEnd));

    /// <summary>The selection, or a fraction of the sound at its start or end.</summary>
    private (int From, int To) RangeOr(double fraction, bool start)
    {
        if (HasSelection) return Selection();
        int n = (int)(samples.Length * fraction);
        return start ? (0, n) : (samples.Length - n, samples.Length);
    }

    private int SampleAt(double x) => waveform.Width <= 0 ? 0 : (int)Math.Clamp(x / waveform.Width * samples.Length, 0, samples.Length);

    private async Task ReplaceAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose an audio file" });
            if (file == null) return;
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            if (Bytes is { } old) undo.Add((old, storedEffect?.Clone()));
            var name = "sounds/" + file.FileName;
            ctx.Adventure.Assets[name] = ms.ToArray();
            sound.AssetName = name;
            sound.Effect = null;
            storedEffect = null;
            ctx.Changed(sound);
            LoadSamples();
            BuildDesigner();
            BuildEditTools();
        }
        catch (Exception ex)
        {
            status.Text = "Import failed: " + ex.Message;
        }
    }

    // =================================================================== helpers

    private static View Heading(string text) => new VerticalStackLayout
    {
        Spacing = 4, Margin = new Thickness(0, 8, 0, 0),
        Children =
        {
            new Label { Text = text, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Theme.Text },
            new BoxView { HeightRequest = 1, Color = Theme.Border },
        },
    };

    private static View Card(string title, params View[] rows)
    {
        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Children.Add(new Label { Text = title.ToUpperInvariant(), FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Theme.SecondaryText, CharacterSpacing = 0.8 });
        foreach (var r in rows) stack.Children.Add(r);
        return new Border
        {
            Content = stack, Padding = new Thickness(12, 10), VerticalOptions = LayoutOptions.Start,
            BackgroundColor = Theme.Pane, Stroke = Theme.Border, StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
        };
    }

    private static Chip Button(string text, string tip, Action action)
    {
        var chip = new Chip(text, tip);
        chip.Clicked += (_, _) => action();
        return chip;
    }

    private sealed class WaveDrawable(SoundEditorView view) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var s = view.samples;
            float mid = rect.Height / 2;
            canvas.StrokeColor = Theme.Border;
            canvas.StrokeSize = 1;
            canvas.DrawLine(0, mid, rect.Width, mid);
            if (s.Length == 0)
            {
                canvas.FontColor = Theme.SecondaryText;
                canvas.FontSize = 13;
                canvas.DrawString(view.notEditable ?? "No audio", rect, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            if (view.HasSelection)
            {
                var (from, to) = view.Selection();
                float x0 = (float)from / s.Length * rect.Width, x1 = (float)to / s.Length * rect.Width;
                canvas.FillColor = Theme.Selected;
                canvas.FillRectangle(x0, 0, x1 - x0, rect.Height);
            }

            // Everything after the cut-off is greyed out.
            if (!view.sound.Repeat && view.sound.CutOffSeconds > 0)
            {
                float cx = (float)(view.sound.CutOffSeconds * view.sampleRate / s.Length * rect.Width);
                if (cx < rect.Width)
                {
                    canvas.FillColor = Colors.Gray.WithAlpha(0.18f);
                    canvas.FillRectangle(cx, 0, rect.Width - cx, rect.Height);
                    canvas.StrokeColor = Theme.Accent;
                    canvas.StrokeDashPattern = new float[] { 4, 3 };
                    canvas.DrawLine(cx, 0, cx, rect.Height);
                    canvas.StrokeDashPattern = null;
                }
            }

            // Min/max of each pixel column.
            canvas.StrokeColor = Theme.Accent;
            int columns = Math.Max(1, (int)rect.Width);
            for (int x = 0; x < columns; x++)
            {
                int a = (int)((long)x * s.Length / columns), b = Math.Max(a + 1, (int)((long)(x + 1) * s.Length / columns));
                float lo = 0, hi = 0;
                for (int i = a; i < b && i < s.Length; i++) { lo = Math.Min(lo, s[i]); hi = Math.Max(hi, s[i]); }
                canvas.DrawLine(x + 0.5f, mid - hi * (mid - 4), x + 0.5f, mid - lo * (mid - 4));
            }
        }
    }
}
