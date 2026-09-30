using AdventureSystem.Core.Packaging;
using AdventureSystem.Core.Samples;
using AdventureSystem.Importers.Common;
using AdventureSystem.Studio.Pages;
using AdventureSystem.Studio.Services;
using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace AdventureSystem.Studio;

/// <summary>
/// The iPad's way in: the system document browser, shown full screen over the Studio. Adventures open in place
/// (wherever they live: On My iPad, iCloud Drive…) and save themselves; a game from another system opens by being
/// imported into a new adventure beside it. "Documents" in the Studio closes the adventure and comes back here.
/// </summary>
public sealed class DocumentBrowser : UIDocumentBrowserViewControllerDelegate
{
    /// <summary>The .adventure type, declared in Info.plist.</summary>
    public const string AdventureType = "uk.co.allthejohnsons.adventure";

    private static DocumentBrowser? instance;
    private readonly StudioPage studio;
    private readonly UIDocumentBrowserViewController browser;
    private bool presenting, dismissWhenPresented;

    private DocumentBrowser(StudioPage studio)
    {
        this.studio = studio;
        browser = new UIDocumentBrowserViewController(ContentTypes())
        {
            AllowsDocumentCreation = true,
            AllowsPickingMultipleItems = false,
            ModalPresentationStyle = UIModalPresentationStyle.FullScreen,
            Delegate = this,
        };
    }

    /// <summary>Adventures, and every game another system's importer reads (plain .txt is left out: it would list every text file).</summary>
    private static UTType[] ContentTypes()
    {
        var types = new List<UTType>();
        if (UTType.CreateFromIdentifier(AdventureType) is { } adventure) types.Add(adventure);
        foreach (var ext in ImporterRegistry.AllExtensions.Where(e => !e.Equals("txt", StringComparison.OrdinalIgnoreCase)))
            if (UTType.CreateFromExtension(ext) is { } t && types.All(x => x.Identifier != t.Identifier)) types.Add(t);
        return types.ToArray();
    }

    /// <summary>Shows the browser over the Studio (at launch, and when an adventure is closed).</summary>
    public static void Show(StudioPage studio, bool animated)
    {
        instance ??= new DocumentBrowser(studio);
        var root = Platform.GetCurrentUIViewController();
        if (root == null || root == instance.browser || instance.browser.PresentingViewController != null) return;
        instance.presenting = true;
        root.PresentViewController(instance.browser, animated, () =>
        {
            instance.presenting = false;
            if (instance.dismissWhenPresented) instance.Dismiss();
        });
    }

    /// <summary>Takes the browser away (once it has finished appearing, if an adventure opened while it was on its way).</summary>
    private void Dismiss()
    {
        dismissWhenPresented = presenting;
        if (!presenting && browser.PresentingViewController != null) browser.DismissViewController(true, null);
    }

    private const string LastDocumentKey = "last-document";

    /// <summary>
    /// At launch: straight into the Studio with the adventure that was open last (wherever it lives), or a new one
    /// in the app's own folder (On My iPad › Adventure System) when there's none. Documents opens the browser.
    /// </summary>
    public static void StartOnStudio(StudioPage studio)
    {
        instance ??= new DocumentBrowser(studio);
        if (instance.ReopenLast()) return;
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var path = Path.Combine(folder, "Untitled Adventure" + AdventurePackage.Extension);
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"Untitled Adventure {n}{AdventurePackage.Extension}");
        AdventurePackage.Save(StudioDocument.CreateNew().Adventure, path);
        instance.Open(NSUrl.FromFilename(path));
    }

    private bool ReopenLast()
    {
        var saved = Preferences.Default.Get(LastDocumentKey, "");
        if (saved.Length == 0) return false;
        try
        {
            var url = NSUrl.FromBookmarkData(NSData.FromArray(Convert.FromBase64String(saved)), NSUrlBookmarkResolutionOptions.WithoutUI, null, out _, out var error);
            if (url == null || error != null) return false;
            return Open(url, reportFailure: false);
        }
        catch { return false; }
    }

    /// <summary>Remembers where the open adventure lives, so the next launch reopens it (a bookmark keeps access to files outside the app).</summary>
    private static void RememberLast(NSUrl url)
    {
        var bookmark = url.CreateBookmarkData(0, Array.Empty<string>(), null, out var error);
        if (bookmark != null && error == null) Preferences.Default.Set(LastDocumentKey, Convert.ToBase64String(bookmark.ToArray()));
    }

    /// <summary>Opens an adventure file in the app's own folder (a game just imported from inside the Studio).</summary>
    public static void OpenFile(StudioPage studio, string path)
    {
        instance ??= new DocumentBrowser(studio);
        instance.Open(NSUrl.FromFilename(path));
    }

    // ------------------------------------------------------------------ browser delegate

    public override void DidRequestDocumentCreation(UIDocumentBrowserViewController controller, Action<NSUrl, UIDocumentBrowserImportMode> importHandler)
    {
        var sheet = UIAlertController.Create("New Adventure", null, UIAlertControllerStyle.ActionSheet);
        void Offer(string title, Func<Core.Model.Adventure> make) => sheet.AddAction(UIAlertAction.Create(title, UIAlertActionStyle.Default, _ =>
        {
            var adventure = make();
            var name = StandaloneExporter.SafeFileName(adventure.Title) + AdventurePackage.Extension;
            var path = Path.Combine(Path.GetTempPath(), name);
            AdventurePackage.Save(adventure, path);
            importHandler(NSUrl.FromFilename(path), UIDocumentBrowserImportMode.Move);
        }));
        Offer("Blank Adventure", () => StudioDocument.CreateNew().Adventure);
        Offer("The Lighthouse (example)", ExampleAdventures.Lighthouse);
#if DEBUG
        Offer("Genesis (Doctor Who fan adventure)", ExampleAdventures.Genesis);
#endif
        sheet.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ => importHandler(null!, UIDocumentBrowserImportMode.None)));
        if (sheet.PopoverPresentationController is { } popover)
        {
            // The create button isn't reachable from here: anchor the sheet to the middle of the top edge.
            popover.SourceView = controller.View;
            popover.SourceRect = new CoreGraphics.CGRect(controller.View!.Bounds.Width / 2, 60, 1, 1);
        }
        controller.PresentViewController(sheet, true, null);
    }

    public override void DidPickDocumentsAtUrls(UIDocumentBrowserViewController controller, NSUrl[] documentUrls)
    {
        if (documentUrls.FirstOrDefault() is { } url) Open(url);
    }

    public override void DidImportDocument(UIDocumentBrowserViewController controller, NSUrl sourceUrl, NSUrl destinationUrl) => Open(destinationUrl);

    public override void FailedToImportDocument(UIDocumentBrowserViewController controller, NSUrl documentUrl, NSError? error) =>
        Alert("Could not create the adventure", error?.LocalizedDescription ?? "");

    // ------------------------------------------------------------------ opening

    private void Open(NSUrl url) => Open(url, reportFailure: true);

    private bool Open(NSUrl url, bool reportFailure)
    {
        bool scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            var bytes = CoordinatedRead(url);
            var path = url.Path ?? url.LastPathComponent ?? "game";
            if (string.Equals(Path.GetExtension(path), AdventurePackage.Extension, StringComparison.OrdinalIgnoreCase))
            {
                var doc = new StudioDocument(AdventurePackage.Load(bytes), path) { Writer = data => CoordinatedWrite(url, data) };
                studio.OpenDocument(doc, () => { if (scoped) url.StopAccessingSecurityScopedResource(); });
                RememberLast(url);
                Dismiss();
                return true;
            }
            Import(url, path, bytes, () => { if (scoped) url.StopAccessingSecurityScopedResource(); });
            return true;
        }
        catch (Exception ex)
        {
            if (scoped) url.StopAccessingSecurityScopedResource();
            if (reportFailure) Alert("Could not open", ex.Message);
            return false;
        }
    }

    /// <summary>A game from another system becomes a new adventure placed next to it, which is then opened.</summary>
    private void Import(NSUrl url, string path, byte[] bytes, Action done)
    {
        var importer = ImporterRegistry.Detect(bytes, path);
        if (importer == null)
        {
            done();
            Alert("Not recognised", "This file isn't a game I can import. I can read PAWS, The Quill (with Illustrator pictures), GAC and Scott Adams games, " +
                                    "Quest 5 (.aslx, .quest), Twine (published .html or .twee) and Z-code stories (.z3–.z8, .zblorb).");
            return;
        }
        ImportResult result;
        try { result = ImporterRegistry.Run(importer, bytes, path); }
        catch { done(); throw; }
        var temp = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(path) + AdventurePackage.Extension);
        AdventurePackage.Save(result.Adventure, temp);
        var report = $"Imported with: {importer.Name}\nFormat: {result.DetectedFormat}\n\n" +
                     $"{result.Adventure.Rooms.Count} rooms, {result.Adventure.Items.Count} items, {result.Adventure.Triggers.Count} triggers, {result.Adventure.Pictures.Count} pictures.\n\n" +
                     (result.Warnings.Count > 0 ? "Warnings:\n• " + string.Join("\n• ", result.Warnings) : "No warnings.");
        browser.ImportDocument(NSUrl.FromFilename(temp), url, UIDocumentBrowserImportMode.Move, (imported, error) =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                done();
                if (error != null || imported == null) { Alert("Could not save the imported adventure", error?.LocalizedDescription ?? ""); return; }
                Open(imported);
                studio.ShowReportWhenReady("Import report", report);
            }));
    }

    // ------------------------------------------------------------------ coordinated file access

    private static byte[] CoordinatedRead(NSUrl url)
    {
        byte[]? data = null;
        Exception? failure = null;
        new NSFileCoordinator().CoordinateRead(url, NSFileCoordinatorReadingOptions.WithoutChanges, out var error, u =>
        {
            try { data = File.ReadAllBytes(u.Path!); } catch (Exception ex) { failure = ex; }
        });
        if (failure != null) throw failure;
        return data ?? throw new IOException(error?.LocalizedDescription ?? "The file couldn't be read.");
    }

    private static void CoordinatedWrite(NSUrl url, byte[] data)
    {
        Exception? failure = null;
        new NSFileCoordinator().CoordinateWrite(url, NSFileCoordinatorWritingOptions.ForReplacing, out var error, u =>
        {
            try { File.WriteAllBytes(u.Path!, data); } catch (Exception ex) { failure = ex; }
        });
        if (failure != null) throw failure;
        if (error != null) throw new IOException(error.LocalizedDescription);
    }

    private void Alert(string title, string message)
    {
        var alert = UIAlertController.Create(title, message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
        (browser.PresentingViewController != null ? browser : Platform.GetCurrentUIViewController())?.PresentViewController(alert, true, null);
    }
}
