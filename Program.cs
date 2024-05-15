using CommandLine;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.Core.WebApi.Types;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkItemExporter;

await Parser.Default.ParseArguments<Options>(args)
    .WithParsedAsync(async o =>
    {
        try
        {
            string teamProjectName = o.TeamProjectName;
            var connection = new VssConnection(o.OrganizationUrl, new VssBasicCredential(string.Empty, o.PersonalAccessToken));
            var projHttpClient = connection.GetClient<ProjectHttpClient>();
            var teamHttpClient = connection.GetClient<TeamHttpClient>();
            var workHttpClient = connection.GetClient<WorkHttpClient>();
            var witrackClient = connection.GetClient<WorkItemTrackingHttpClient>();
            IPagedList<TeamProjectReference> projects = await projHttpClient.GetProjects();


            Console.WriteLine("Preparing export...");
            List<WebApiTeam> teams = await teamHttpClient.GetAllTeamsAsync();
            var project = projects
                .Where(p => string.Equals(p.Name, teamProjectName, StringComparison.InvariantCultureIgnoreCase))
                .FirstOrDefault() ?? throw new Exception("Project " + teamProjectName + " not found");

            Console.WriteLine("Export is ready to begin");

            foreach (WebApiTeam team in teams)
            {
                Console.WriteLine("Exporting team backlog " + team.Name);
                string teamDirectoryPath = Path.Combine(o.OutputPath, SanitizeFilesystemName(team.Name));
                var teamContext = new TeamContext(project.Id, new Guid?(team.Id))
                {
                    Team = team.Name,
                    Project = project.Name
                };
                var backlogs = await workHttpClient.GetBacklogsAsync(teamContext);
                foreach (BacklogLevelConfiguration backlog in backlogs)
                {
                    string backlogName = backlog.Name;
                    string backlogDirectoryPath = EnsureDirectoryCreated(teamDirectoryPath, backlogName);

                    var workItems = await workHttpClient.GetBacklogLevelWorkItemsAsync(teamContext, backlog.Id);
                    int[] workItemIds = workItems.WorkItems.Select(s => s.Target.Id).ToArray();
                    await ExportWorkItems(backlogDirectoryPath, witrackClient, workItemIds);
                }

                int teamWorkItemCount = Directory.GetFiles(teamDirectoryPath, "*.*", SearchOption.AllDirectories).Length;
                Console.WriteLine($"Finished exporting team backlog. {teamWorkItemCount} work items have been exported.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exporter failed with the following error: {ex}");
        }
        finally
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    });



static async Task ExportWorkItems(
  string outputPath,
  WorkItemTrackingHttpClient witrackClient,
  IEnumerable<int> workItemIds)
{
    if (!Enumerable.Any<int>(workItemIds))
        return;
    foreach (var workItemsIdsBucket in workItemIds.Batch(200))
    {
        var workitems = await witrackClient.GetWorkItemsAsync(workItemsIdsBucket, expand: WorkItemExpand.All);
        foreach (WorkItem workItem in workitems)
        {
            if (workItem.Id == null)
                continue;
            var workItemId = workItem.Id.Value;
            var comments = await witrackClient.GetCommentsAsync(workItemId);
            var revisions = await witrackClient.GetRevisionsAsync(workItemId);

            string wiName = (string)workItem.Fields["System.Title"];
            string directoryPath = EnsureDirectoryCreated(outputPath, workItemId.ToString());
            string path1 = directoryPath;

            string workItemFilename = Path.Combine(directoryPath, SanitizeFilesystemName($"{workItemId}__{wiName}")+ ".json");
            var workItemContainer = new WorkItemContainer()
            {
                WorkItem = workItem,
                Comments = comments,
                Revisions = revisions
            };
            await File.WriteAllBytesAsync(workItemFilename, JsonSerializer.SerializeToUtf8Bytes(workItemContainer, options: new()
            {
                WriteIndented = true
            }));

            var childs = Enumerable.Empty<WorkItemRelation>();
            if (workItem.Relations != null)
                childs = workItem.Relations.Where(p => p.Rel == "System.LinkTypes.Hierarchy-Forward");
            if (childs.Any())
            {
                string childDirectory = Path.Combine(directoryPath, "childs");
                if (!Directory.Exists(childDirectory))
                    Directory.CreateDirectory(childDirectory);
                var childWorkItemIds = new List<int>();
                foreach (WorkItemRelation child in childs)
                {
                    int lastUrlSlash = child.Url.LastIndexOf('/');
                    if (lastUrlSlash < 0)
                        throw new Exception("Cannot find child id");

                    string childIdString = child.Url[(lastUrlSlash + 1)..];
                    int childId = int.Parse(childIdString);
                    childWorkItemIds.Add(childId);
                }
                await ExportWorkItems(childDirectory, witrackClient, childWorkItemIds);
            }
        }
    }
}

static string EnsureDirectoryCreated(string outputPath, string name)
{
    string path = Path.Combine(outputPath, SanitizeFilesystemName(name));
    if (!Directory.Exists(path))
        Directory.CreateDirectory(path);
    return path;
}

static string SanitizeFilesystemName(string name)
{
    string str = FilenameSanitizationRegex().Replace(name, "_");
    return str.Length > 30 ? str[..30] : str;
}

partial class Program
{
    [GeneratedRegex("[^a-zA-Z0-9_.-]+")]
    private static partial Regex FilenameSanitizationRegex();
}