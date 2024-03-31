using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace prmonitor;

internal class CommunityPRsReportDataRetriever : GitHubDataRetrieverBase
{
    private int _cutoffDaysForInactiveCommunityPRs;
    private string _communityContributionLabel;

    public CommunityPRsReportDataRetriever(GitHubClient client, string org, string repo, int cutoffDaysForInactiveCommunityPRs, string communityContributionLabel) : base(client, org, repo)
    {
        _cutoffDaysForInactiveCommunityPRs = cutoffDaysForInactiveCommunityPRs;
        _communityContributionLabel = communityContributionLabel;
    }

    public async Task<List<(PullRequest, DateTimeOffset)>> GetInactiveCommunityPRs()
    {
        var openPRsRequest = new PullRequestRequest()
        {
            State = ItemStateFilter.Open,
            //Base = "main"
        };

        var openPRs = await Client.PullRequest.GetAllForRepository(Org, Repo, openPRsRequest);
        var inactivePrsList = new List<(PullRequest, DateTimeOffset)>();
        DateTimeOffset cutDate = DateTimeOffset.Now.AddDays(-_cutoffDaysForInactiveCommunityPRs);

        foreach (PullRequest pr in openPRs)
        {
            // Ignore non community contribution PRs
            if (!pr.Labels.Any(l => l.Name == _communityContributionLabel))
                continue;

            // Ignore those PRs which are pending author input
            if (pr.Labels.Any(l => l.Name == "pr: pending author input"))
                continue;

            // Ignore draft PRs
            if (pr.Draft)
                continue;

            if (pr.CreatedAt > cutDate)
                continue;

            var prCommits = await Client.PullRequest.Commits(Org, Repo, pr.Number);
            var lastCommitDate = prCommits.Last().Commit.Author.Date;
            if (lastCommitDate > cutDate)
            {
                // There was a recent commit on this PR, so not flagging as `stale
                continue;
            }

            inactivePrsList.Add((pr, lastCommitDate));
        }

        return inactivePrsList;
    }

    public async Task<IReadOnlyList<PullRequest>> GetCompletedCommunityPullRequests(DateTime dateTime)
    {
        const string queryDateFormat = "yyyy-MM-dd";

        var result = new List<PullRequest>();

        var searchResults = await Client.Search.SearchIssues(new SearchIssuesRequest($"is:pr repo:{Org}/{Repo} is:closed label:{_communityContributionLabel} closed:>{dateTime.ToString(queryDateFormat)}"));
        foreach (var item in searchResults.Items)
        {
            result.Add(await Client.PullRequest.Get(Org, Repo, item.Number));
        }

        return result.AsReadOnly();
    }
}
