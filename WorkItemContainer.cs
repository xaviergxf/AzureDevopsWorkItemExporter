using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace WorkItemExporter
{
    public class WorkItemContainer
    {
        public required WorkItem WorkItem { get; init; }

        public required WorkItemComments Comments { get; init; }

        public required List<WorkItem> Revisions { get; init; }
    }
}