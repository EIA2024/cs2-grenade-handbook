using System.Buffers.Binary;
using Handbook.Core;

namespace Handbook.Desktop;

internal static class QuickBrowseTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> assert)
    {
        void Check(bool condition) => assert(condition, "Quick browse assertion failed");
        void Reject(Action action) { bool thrown = false; try { action(); } catch (FormatException) { thrown = true; } Check(thrown); }
        test("Input bindings accept single keys/buttons and reject duplicates/modifier-only", () =>
        {
            var bindings = InputBindings.Defaults(); InputBindings.Validate(bindings);
            bindings[InputCommand.Previous] = new(InputDevice.Keyboard, 65); InputBindings.Validate(bindings);
            bindings[InputCommand.Next] = bindings[InputCommand.Previous]; Reject(() => InputBindings.Validate(bindings));
            bindings[InputCommand.Next] = new(InputDevice.Keyboard, 16); Reject(() => InputBindings.Validate(bindings));
            bindings[InputCommand.Next] = new(InputDevice.Mouse, 3); Reject(() => InputBindings.Validate(bindings));
        });
        test("Input edges suppress held/repeated keys and require release across confirmation", () =>
        {
            var bindings = InputBindings.Defaults(); bindings[InputCommand.Confirm] = new(InputDevice.Keyboard, 67); var matcher = new InputCommandMatcher(bindings);
            Check(matcher.Feed(InputDevice.Keyboard, 67, true) == InputCommand.Confirm);
            Check(matcher.Feed(InputDevice.Keyboard, 67, true) == null); Check(matcher.Feed(InputDevice.Keyboard, 67, false) == null);
            Check(matcher.Feed(InputDevice.Keyboard, 67, true) == InputCommand.Confirm);
            Check(matcher.Feed(InputDevice.Mouse, 5, true) == InputCommand.Previous); Check(matcher.Feed(InputDevice.Mouse, 5, true) == null);
            matcher.Feed(InputDevice.Mouse, 5, false); Check(matcher.Feed(InputDevice.Mouse, 5, true) == InputCommand.Previous);
        });
        test("Modifier chords match exactly; reset and rebinding clear transient state", () =>
        {
            var bindings = InputBindings.Defaults(); bindings[InputCommand.Confirm] = new(InputDevice.Mouse, 4, InputModifiers.Shift); var matcher = new InputCommandMatcher(bindings);
            matcher.Feed(InputDevice.Keyboard, 162, true); matcher.Feed(InputDevice.Keyboard, 164, true); Check(matcher.Feed(InputDevice.Keyboard, 81, true) == InputCommand.ToggleMenu);
            matcher.Feed(InputDevice.Keyboard, 81, false); matcher.Feed(InputDevice.Keyboard, 160, true); Check(matcher.Feed(InputDevice.Keyboard, 81, true) == null);
            matcher.Reset(); Check(matcher.Feed(InputDevice.Keyboard, 81, true) == null);
            matcher.Feed(InputDevice.Keyboard, 160, true); matcher.Feed(InputDevice.Keyboard, 161, true); matcher.Feed(InputDevice.Keyboard, 160, false);
            Check(matcher.Feed(InputDevice.Mouse, 4, true) == InputCommand.Confirm);
            matcher.SetBindings(InputBindings.Defaults()); Check(matcher.Feed(InputDevice.Mouse, 4, true) == InputCommand.Confirm);
        });
        test("Raw packet decoder ignores pointer movement/wheel and parses keyboard/button releases", () =>
        {
            var events = new List<(InputDevice, int, bool)>(); var packet = new byte[RawPacketDecoder.HeaderSize + 24];
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(RawPacketDecoder.HeaderSize + 12), 300);
            RawPacketDecoder.Decode(packet, (d, k, p) => events.Add((d, k, p))); Check(events.Count == 0);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(RawPacketDecoder.HeaderSize + 4), 0x400); RawPacketDecoder.Decode(packet, (d, k, p) => events.Add((d, k, p))); Check(events.Count == 0);
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(RawPacketDecoder.HeaderSize + 4), 64 | 128); RawPacketDecoder.Decode(packet, (d, k, p) => events.Add((d, k, p))); Check(events.SequenceEqual(new[] { (InputDevice.Mouse, 5, true), (InputDevice.Mouse, 5, false) }));
            events.Clear(); Array.Clear(packet); BinaryPrimitives.WriteInt32LittleEndian(packet, 1); BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(RawPacketDecoder.HeaderSize + 6), 17); BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(RawPacketDecoder.HeaderSize + 2), 3);
            RawPacketDecoder.Decode(packet, (d, k, p) => events.Add((d, k, p))); Check(events.Single() == (InputDevice.Keyboard, 163, false));
        });
        var a = Path.Combine("C:\\test", "合集", "Dust2", "CT", "A大"); var leafPath = Path.Combine(a, "烟"); var nested = Path.Combine(leafPath, "子分类", "闪");
        Entry EntryAt(string path, bool valid = true) => new(path, "合集", "Dust2", "CT", Path.GetFileName(path), valid ? "s.png" : null, "a.png", "说明", valid ? [] : ["草稿"]);
        var one = EntryAt(leafPath); var two = EntryAt(nested); var draft = EntryAt(Path.Combine(a, "草稿"), false);
        LibraryNode Node(string path, NodeKind kind, params LibraryNode[] children) => new(path, Path.GetFileName(path), kind, children);
        var sidePath = Path.GetDirectoryName(a)!; var mapPath = Path.GetDirectoryName(sidePath)!; var collectionPath = Path.GetDirectoryName(mapPath)!;
        var tree = Node(collectionPath, NodeKind.Collection, Node(mapPath, NodeKind.Map, Node(sidePath, NodeKind.Side,
            Node(a, NodeKind.Folder, Node(leafPath, NodeKind.Entry, Node(Path.GetDirectoryName(nested)!, NodeKind.Folder, Node(nested, NodeKind.Entry))), Node(draft.Path, NodeKind.Entry)), Node(Path.Combine(sidePath, "空"), NodeKind.Folder))));
        var snapshot = new LibrarySnapshot([tree], [one, two, draft], []);
        test("Navigation prunes drafts/empty folders; wraps and returns through every level", () =>
        {
            var nav = new QuickNavigation(); nav.Update(snapshot); nav.Open(one.Path);
            Check(nav.Folder == a && nav.Items.Count == 2 && nav.Selected!.Id == leafPath);
            nav.Move(1); Check(nav.Selected!.Back); nav.Move(-1); Check(nav.Selected!.Id == leafPath);
            nav.Move(-1); nav.Confirm(); Check(nav.Folder == sidePath); Check(nav.Items.Count == 2);
            for (int i = 0; i < 3; i++) { while (!nav.Selected!.Back) nav.Move(-1); nav.Confirm(); }
            Check(nav.Folder == "" && nav.Items.Count == 1);
        });
        test("Mixed entry/directory shows own entry separately; confirm/cancel/restore are deterministic", () =>
        {
            var nav = new QuickNavigation(); nav.Update(snapshot); nav.Open(one.Path); Check(nav.Confirm() == null && nav.Folder == leafPath);
            Check(nav.Selected!.Id == "$self"); Check(nav.Confirm() == one && !nav.IsOpen);
            nav.Open(null, leafPath, "$self"); nav.Move(1); var id = nav.Selected!.Id; nav.Cancel(); Check(!nav.IsOpen); Check(nav.Confirm() == null);
            nav.Open(null, leafPath, id); nav.Confirm(); Check(nav.Folder == Path.GetDirectoryName(nested)); Check(nav.Confirm() == two);
        });
        test("Empty and stale navigation states remain cancellable without selecting a draft", () =>
        {
            var nav = new QuickNavigation(); nav.Update(new([], [], [])); nav.Open("missing", "missing", "missing"); nav.Move(1); Check(nav.Confirm() == null); nav.Cancel(); Check(!nav.IsOpen);
            nav.Update(snapshot); nav.Open(one.Path, "missing", "missing"); Check(nav.Folder == a); nav.Cancel();
        });
    }
}
