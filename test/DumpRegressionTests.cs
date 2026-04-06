using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using fdata_dump;

namespace fdata_dump.Tests
{
    public class DumpRegressionTests
    {
        [Theory]
        [InlineData("v1", FdataMode.FEThreeHopes)]
        [InlineData("v2", FdataMode.Default)]
        public async Task Dump_Matches_ExpectedOutput(string variant, FdataMode mode)
        {
            string repoRoot = FindRepoRoot();
            string fixtureRoot = Path.Combine(repoRoot, "test", "fdata_files", variant);
            string fixtureInputs = Path.Combine(fixtureRoot, "files");
            string expectedRoot = Path.Combine(fixtureRoot, "output");

            Assert.True(Directory.Exists(fixtureInputs), $"Missing fixture inputs at {fixtureInputs}");
            Assert.True(Directory.Exists(expectedRoot), $"Missing expected output at {expectedRoot}");

            string tempDir = Path.Combine(Path.GetTempPath(), "fdata_dump_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyDirectory(fixtureInputs, tempDir);

                await Program.RunAsync(tempDir, mode);

                string actualRoot = Path.Combine(tempDir, "fdata_out");
                Assert.True(Directory.Exists(actualRoot), $"Dumper did not produce {actualRoot}");

                AssertDirectoriesEqual(expectedRoot, actualRoot);
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
            }
        }

        private static void AssertDirectoriesEqual(string expectedRoot, string actualRoot)
        {
            // Every expected file must exist in actual output with identical bytes.
            foreach (string expectedFile in Directory.EnumerateFiles(expectedRoot, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(expectedRoot, expectedFile);
                string actualFile = Path.Combine(actualRoot, rel);
                Assert.True(File.Exists(actualFile), $"Expected file missing in dump output: {rel}");

                byte[] expectedBytes = File.ReadAllBytes(expectedFile);
                byte[] actualBytes = File.ReadAllBytes(actualFile);
                Assert.True(
                    expectedBytes.AsSpan().SequenceEqual(actualBytes),
                    $"Byte mismatch for {rel} (expected {expectedBytes.Length}B, got {actualBytes.Length}B)");
            }

            // Actual output must contain no files beyond what was expected (ignore generated filelist csv).
            foreach (string actualFile in Directory.EnumerateFiles(actualRoot, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(actualRoot, actualFile);
                if (string.Equals(rel, "filelist-fdata-rdb.csv", StringComparison.OrdinalIgnoreCase))
                    continue;
                string expectedFile = Path.Combine(expectedRoot, rel);
                Assert.True(File.Exists(expectedFile), $"Unexpected file produced by dumper: {rel}");
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
            }
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(source, file);
                File.Copy(file, Path.Combine(dest, rel), overwrite: true);
            }
        }

        private static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "fdata_dump.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Could not locate repo root (fdata_dump.sln) from " + AppContext.BaseDirectory);
        }
    }
}
