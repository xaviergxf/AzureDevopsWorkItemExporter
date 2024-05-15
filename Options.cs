using CommandLine;

namespace WorkItemExporter
{

    public class Options
    {
        [Option("org", Required = true, HelpText = "The Azure DevOps organization Url. Ex: https://dev.azure.com/abc ")]
        public required Uri OrganizationUrl { get; init; }

        [Option("pat", Required = true, HelpText = "The Azure DevOps Personal Access Token (PAT)")]
        public required string PersonalAccessToken { get; init; }

        [Option("proj", Required = true, HelpText = "The Azure DevOps project name")]
        public required string TeamProjectName { get; init; }

        [Option("output", Required = false, HelpText = "The output of the export")]
        public string OutputPath { get; init; } = Path.Combine(Directory.GetCurrentDirectory(), "output");
    }
}