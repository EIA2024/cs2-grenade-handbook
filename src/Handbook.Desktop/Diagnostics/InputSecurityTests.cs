using System.Text.Json;
using Handbook.Core;

namespace Handbook.Desktop;

internal static class InputSecurityTests
{
    public static void Run(Action<string, Action> test, Action<bool, string> assert)
    {
        test("Damaged nullable settings recover before migration and presentation", () =>
        {
            var settings = JsonSerializer.Deserialize<UserSettings>("""
                {"Recent":null,"BrowseFolder":null,"BrowseItem":null,"SelectedPath":null,
                 "SelectionHotkey":null,"VisibilityHotkey":null,"Width":0,"Height":-1,"Opacity":3}
                """)!;
            var warnings = SettingsValidation.Normalize(settings);
            assert(warnings.Count > 0, "Recovery must report damaged fields");
            assert(settings.Recent.Count == 0 && settings.BrowseFolder == "" && settings.BrowseItem == "" && settings.SelectedPath == "", "Null fields were not recovered");
            assert(settings.Width == 520 && settings.Height == 380 && settings.Opacity == 1, "Invalid geometry was not recovered");
            InputBindings.Migrate(settings, BindingNames.ParseLegacy);
            InputBindings.Validate(settings.InputBindings!);
            assert(settings.SettingsVersion == 2, "Legacy null bindings did not migrate");
        });
        test("Settings recovery preserves valid choices and bounds recent entries", () =>
        {
            var settings = new UserSettings { Width = 700, Height = 450, Left = -1300, Top = 20, Opacity = .6,
                Recent = [null!, "", "A", "a", "B"], SelectedPath = "合集/Dust2/CT/烟", InputBindings = InputBindings.Defaults(), SettingsVersion = 2 };
            settings.InputBindings[InputCommand.Confirm] = new(InputDevice.Keyboard, 67);
            SettingsValidation.Normalize(settings); InputBindings.Migrate(settings);
            assert(settings.Recent.SequenceEqual(new[] { "A", "B" }), "Recent choices must be non-null and unique");
            assert(settings.Left == -1300 && settings.Width == 700 && settings.Height == 450 && settings.Opacity == .6, "Valid multi-monitor geometry changed");
            assert(settings.InputBindings[InputCommand.Confirm].Code == 67 && settings.SelectedPath == "合集/Dust2/CT/烟", "Valid selection or binding changed");
            settings.Left = double.PositiveInfinity; settings.Top = double.NaN; settings.Width = double.NaN;
            SettingsValidation.Normalize(settings);
            assert(settings.Left == -1 && settings.Top == -1 && settings.Width == 520, "Nonfinite geometry was not repaired");
        });
    }
}
