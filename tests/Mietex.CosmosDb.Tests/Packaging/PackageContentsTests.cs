using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Mietex.CosmosDb.Tests.Packaging;

[Trait("Category", "Packaging")]
public sealed class PackageContentsTests
{
    [Fact]
    public async Task PackageContainsBothTfmsDocumentationAndLicence()
    {
#if !NET10_0_OR_GREATER
        Assert.Skip("Packaging test runs on net10.0 only.");
        return;
#else
        var repositoryRoot = FindRepositoryRoot();
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"mietex-pack-{Guid.NewGuid():N}");

        try
        {
            await PackAsync(repositoryRoot, outputDirectory, TestContext.Current.CancellationToken);

            var packagePath = Assert.Single(
                Directory.GetFiles(outputDirectory, "Mietex.CosmosDb.*.nupkg"));
            var symbolPath = Assert.Single(
                Directory.GetFiles(outputDirectory, "Mietex.CosmosDb.*.snupkg"));

            using (var archive = ZipFile.OpenRead(packagePath))
            {
                var names = archive.Entries.Select(entry => entry.FullName).ToList();

                Assert.Contains("lib/net8.0/Mietex.CosmosDb.dll", names);
                Assert.Contains("lib/net10.0/Mietex.CosmosDb.dll", names);
                Assert.Contains("lib/net8.0/Mietex.CosmosDb.xml", names);
                Assert.Contains("lib/net10.0/Mietex.CosmosDb.xml", names);
                Assert.Contains("README.md", names);
                Assert.Contains("LICENSE", names);

                var nuspec = archive.Entries.Single(
                    entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
                using var reader = new StreamReader(nuspec.Open(), Encoding.UTF8);
                var metadata = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
                Assert.Contains("<id>Mietex.CosmosDb</id>", metadata);
                Assert.Contains("<license type=\"expression\">MIT</license>", metadata);
                Assert.Contains("github.com/pmieten/MietexCosmosDb", metadata);
            }

            Assert.True(new FileInfo(symbolPath).Length > 0);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
#endif
    }

    [Fact]
    public async Task PackingIsDeterministicForManagedAssemblies()
    {
#if !NET10_0_OR_GREATER
        Assert.Skip("Packaging test runs on net10.0 only.");
        return;
#else
        var repositoryRoot = FindRepositoryRoot();
        var firstDirectory = Path.Combine(Path.GetTempPath(), $"mietex-pack-a-{Guid.NewGuid():N}");
        var secondDirectory = Path.Combine(Path.GetTempPath(), $"mietex-pack-b-{Guid.NewGuid():N}");

        try
        {
            await PackAsync(repositoryRoot, firstDirectory, TestContext.Current.CancellationToken);
            await PackAsync(repositoryRoot, secondDirectory, TestContext.Current.CancellationToken);

            var first = FindPackage(firstDirectory);
            var second = FindPackage(secondDirectory);

            Assert.Equal(
                HashAssembly(first, "lib/net10.0/Mietex.CosmosDb.dll"),
                HashAssembly(second, "lib/net10.0/Mietex.CosmosDb.dll"));
        }
        finally
        {
            DeleteIfExists(firstDirectory);
            DeleteIfExists(secondDirectory);
        }
#endif
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Mietex.CosmosDb.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static async Task PackAsync(
        string repositoryRoot,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add(Path.Combine(
            repositoryRoot,
            "src",
            "Mietex.CosmosDb",
            "Mietex.CosmosDb.csproj"));
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputDirectory);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("-p:PackageVersion=0.0.0-test");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet pack.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.True(
            process.ExitCode == 0,
            $"dotnet pack failed with exit code {process.ExitCode}.{Environment.NewLine}" +
            $"{standardOutput}{Environment.NewLine}{standardError}");
    }

    private static string FindPackage(string directory) =>
        Directory.GetFiles(directory, "Mietex.CosmosDb.*.nupkg").Single();

    private static string HashAssembly(string packagePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        using var stream = archive.GetEntry(entryName)?.Open()
            ?? throw new InvalidOperationException($"Missing package entry '{entryName}'.");
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void DeleteIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
