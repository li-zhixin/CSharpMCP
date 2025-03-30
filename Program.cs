using Microsoft.Extensions.Hosting;
using ModelContextProtocol;

namespace CSharpMCP;

class Program
{
    static async Task Main(string[] args)
    {
        DotnetInteractiveTool.ScriptOptions = new DotnetScriptOption(args.Length == 1 ? args[0] : string.Empty).Options;
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
        await builder.Build().RunAsync();
    }
}