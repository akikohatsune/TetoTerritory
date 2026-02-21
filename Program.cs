using TetoTerritory.CSharp.Core;

try
{
    DotEnvLoader.Load();
    var settings = Settings.Load();

    await using var bot = new DiscordBot(settings);
    await bot.RunAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
