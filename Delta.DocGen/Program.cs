using System.CommandLine;
using DocGenRootCommand = Delta.DocGen.CLI.RootCommand;

return await DocGenRootCommand.Build(Delta.DocGen.CLI.CliRunner.Run).InvokeAsync(args);
