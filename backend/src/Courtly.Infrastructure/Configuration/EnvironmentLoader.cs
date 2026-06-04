namespace Courtly.Infrastructure.Configuration;

/// <summary>
/// Loads the nearest <c>.env</c> file (walking up from the working directory to the repo root) into
/// process environment variables, so the host's <c>AddEnvironmentVariables()</c> config picks them up.
/// Best-effort: in containers the file is absent and config comes from docker-compose, so this is a no-op.
/// </summary>
public static class EnvironmentLoader
{
    public static void Load()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, ".env");
            if (File.Exists(candidate))
            {
                DotNetEnv.Env.Load(candidate);
                return;
            }

            directory = directory.Parent;
        }
    }
}
