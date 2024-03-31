using Octokit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace prmonitor;

internal class HelpWantedIssuesDataRetriever : GitHubDataRetrieverBase
{
    private const string HelpWantedLabel = "help wanted";

    public HelpWantedIssuesDataRetriever(GitHubClient client, string org, string repo) : base(client, org, repo)
    {
    }

    public async Task<Dictionary<string, List<Issue>>> RetrieveHelpWantedIssuesConvertedSinceAsync(DateTimeOffset since)
    {
        var result = new Dictionary<string, List<Issue>>();
        var request = new RepositoryIssueRequest
        {
            State = ItemStateFilter.Open,
            Since = since
        };

        request.Labels.Add(HelpWantedLabel);

        var recentlyUpdatedHelpWantedIssues = await Client.Issue.GetAllForRepository(Org, Repo, request);

        foreach (var item in recentlyUpdatedHelpWantedIssues)
        {
            var userWhoAddedHelpWantedLabel = await HelpWantedLabelAddedRecentlyAsync(item, since);
            if (userWhoAddedHelpWantedLabel is null)
            {
                // This issue wasn't labeled with `help wanted` label recently
                continue;
            }

            var userLogin = userWhoAddedHelpWantedLabel.ToLowerInvariant() ?? string.Empty;
            List<Issue> list;
            if (!result.TryGetValue(userLogin, out list))
            {
                list = new List<Issue>();
                result.Add(userLogin, list);
            }

            list.Add(item);
        }

        return result;
    }

    private async Task<string?> HelpWantedLabelAddedRecentlyAsync(Issue item, DateTimeOffset since)
    {
        var events = await Client.Issue.Events.GetAllForIssue(Org, Repo, item.Number);
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].CreatedAt >= since && events[i].Event.Value == EventInfoState.Labeled && events[i].Label.Name == HelpWantedLabel)
                return events[i].Actor.Login;
        }

        return null;
    }
}
