using Bookletter;
using Bookletter.Cli;

try
{
    var options = CliOptions.Parse(args);
    if (options is null)
        return 0;

    new BookletGenerator(options).Run();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
