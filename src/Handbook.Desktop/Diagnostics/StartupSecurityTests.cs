using System.Text;

namespace Handbook.Desktop;

internal static class StartupSecurityTests
{
    internal static void Run(string testRoot, Action<string, Action> test, Action<bool, string> assert)
    {
        test("Untrusted data-location text falls back without network access or startup failure", () =>
        {
            var pointer = Path.Combine(testRoot, "location-test.txt");
            foreach (var value in new[] { "", "relative", @"\\server\share", @"\\?\C:\data", "C:\\data:alternate" })
            {
                File.WriteAllText(pointer, value);
                assert(DataRootLocator.Read(pointer, out var warning) == null && warning != null, "Invalid location was accepted");
            }
            File.WriteAllBytes(pointer, [0xff, 0xff]);
            assert(DataRootLocator.Read(pointer, out _) == null, "Invalid UTF-8 was accepted");
            File.WriteAllText(pointer, new string('a', 17000), Encoding.UTF8);
            assert(DataRootLocator.Read(pointer, out _) == null, "Oversize pointer was accepted");
            File.WriteAllText(pointer, testRoot);
            assert(DataRootLocator.Read(pointer, out _) == testRoot, "Valid path did not restore");
            using var locked = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.None);
            assert(DataRootLocator.Save(pointer, testRoot) != null, "Locked pointer save should return a warning");
        });
    }
}
