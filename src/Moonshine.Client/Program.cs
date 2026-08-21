using Moonshine.App;

if (!MoonshineApplication.TryParseRole(args, out ApplicationRole role))
{
    Console.Error.WriteLine("Usage: Moonshine --role host|client|host-client");
    return 2;
}

using var application = new MoonshineApplication();
ApplicationStartResult result = application.Start(role);
Console.WriteLine($"Moonshine role: {role}");
Console.WriteLine(result.Message);
return result.IsStarted ? 0 : 1;
