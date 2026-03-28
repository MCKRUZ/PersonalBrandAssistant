namespace PersonalBrandAssistant.Infrastructure.Tests.Docker;

public class DockerComposeValidationTests
{
    private static string GetComposeFilePath()
    {
        // Walk up from bin/Debug/net10.0 to find the repo root with docker-compose.yml
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var composeFile = Path.Combine(dir.FullName, "docker-compose.yml");
            if (File.Exists(composeFile))
                return composeFile;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("docker-compose.yml not found in any parent directory.");
    }

    [Fact]
    public void SidecarService_NotPublishedToExternalPorts()
    {
        var composeContent = File.ReadAllText(GetComposeFilePath());

        // Extract the sidecar service block (from "  sidecar:" to next service or end)
        var sidecarIndex = composeContent.IndexOf("  sidecar:", StringComparison.Ordinal);
        if (sidecarIndex < 0)
        {
            Assert.Fail("Sidecar service not found in docker-compose.yml");
            return;
        }

        // Find the next top-level service (2 spaces + word + colon at start of line)
        var afterSidecar = composeContent[(sidecarIndex + 10)..];
        var nextServiceMatch = System.Text.RegularExpressions.Regex.Match(
            afterSidecar, @"^\s{2}\w+:", System.Text.RegularExpressions.RegexOptions.Multiline);

        var sidecarBlock = nextServiceMatch.Success
            ? afterSidecar[..nextServiceMatch.Index]
            : afterSidecar;

        Assert.DoesNotContain("ports:", sidecarBlock);
    }
}
