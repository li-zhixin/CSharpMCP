# CSharpMCP


The CSharpMCP is a mcp server designed for executing C# code base roslyn.

> **Warning**: This will execute code on the local machine. Use with caution. 


## Available Tools

- RunAsync: Execute the provided C# code, and the state (like variables) will be preserved each time.

- CleanExecuteContext: Clean the code execution context, all states will be cleared.

- GetHistoryCode: Get the history code.

## How to reference existing projects

1. Compile the existing project
2. Use the path of the compiled dll as the startup parameter of CSharpMCP (references will be added automatically)

