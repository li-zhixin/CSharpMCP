using System.ComponentModel;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using ModelContextProtocol.Server;

namespace CSharpMCP;

[McpServerToolType, Description("the C# code executor based on Roslyn C# script engine")]
public class DotnetInteractiveTool
{
    public static ScriptOptions? ScriptOptions;

    private static ScriptState? _scriptState;

    private static string CodeHistory = string.Empty;
        
    [McpServerTool, Description("Execute the provided C# code, and the state (like variables) will be preserved each time")]
    public async Task<string?> RunAsync(string code)
    {
        ArgumentNullException.ThrowIfNull(ScriptOptions);
        code += Environment.NewLine;
        CodeHistory += code;
        if (_scriptState == null)
        {
            _scriptState = await CSharpScript.RunAsync(Environment.NewLine + code, ScriptOptions);
        }
        else
        {
            _scriptState = await _scriptState.ContinueWithAsync(code);
        }

        return _scriptState.ReturnValue?.ToString();
    }

    [McpServerTool, Description("Clean the code execution context, all previous states will be cleared.")]
    public void CleanExecuteContext()
    {
        _scriptState = null;
        CodeHistory = string.Empty;
    }
    
    [McpServerTool, Description("Get the history code")]
    public string GetHistoryCode()
    {
        return CodeHistory;
    }
}