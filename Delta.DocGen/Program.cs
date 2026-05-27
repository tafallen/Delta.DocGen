using System.CommandLine;  // required for InvokeAsync extension on RootCommand
using Delta.DocGen.CLI;

return await CliRootCommand.Build(CliRunner.Run).InvokeAsync(args);
