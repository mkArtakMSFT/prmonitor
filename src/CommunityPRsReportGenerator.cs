using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace prmonitor;

internal class CommunityPRsReportGenerator
{
    private GitHubClient _client;
    private string _org;
    private string _repo;
    private int _cutoffDaysForMergedPRs;
    private int _cutoffDaysForInactiveCommunityPRs;
    private string _communityContributionLabel;
    private IUserNameResolver _userNameResolver;

    public CommunityPRsReportGenerator(GitHubClient client, IUserNameResolver userNameResolver, string org, string repo, int cutoffDaysForMergedPRs, int cutoffDaysForInactiveCommunityPRs, string communityContributionLabel)
    {
        _client = client;
        _org = org;
        _repo = repo;
        _cutoffDaysForMergedPRs = cutoffDaysForMergedPRs;
        _cutoffDaysForInactiveCommunityPRs = cutoffDaysForInactiveCommunityPRs;
        _communityContributionLabel = communityContributionLabel;
        _userNameResolver = userNameResolver;
    }

    public async Task<string> GenerateReportForMergedPullRequests()
    {
        var mergedCommunityPRs = await GetMergedCommunityPullRequests(_org, _repo, DateTime.Now.AddDays(-_cutoffDaysForMergedPRs));

        if (mergedCommunityPRs.Count == 0)
            return string.Empty;

        var result = new StringBuilder();
        var mergedPRsByAuthors = new Dictionary<string, int>();

        foreach (var pr in mergedCommunityPRs)
        {
            var mergedBy = pr.MergedBy.Login;

            if (mergedPRsByAuthors.TryGetValue(mergedBy, out var count))
                count++;
            else
                count = 1;

            mergedPRsByAuthors[mergedBy] = count;
        }
        result.Append($"<br /><div style='font-weight:bold'>Community PRs merged during last {_cutoffDaysForMergedPRs} days</div>");
        result.Append("<table><tr><th>Merged By</th><th>Number of PRs merged</th></tr>");
        foreach (var item in mergedPRsByAuthors.OrderByDescending(i => i.Value))
        {
            result.AppendLine(await GenerateHtmlTemplateForMergedPR(item.Key, item.Value));
        }

        result.Append("</table>");

        return result.ToString();
    }

    public async Task<string> GetInactiveCommunityPRsReport()
    {
        List<(PullRequest, DateTimeOffset)> pullRequests = await GetInactiveCommunityPRs();

        using StringWriter sw = new StringWriter();

        Dictionary<PullRequest, string?> pr_area = new Dictionary<PullRequest, string?>();
        foreach (var item in pullRequests)
        {
            var pr = item.Item1;
            pr_area.Add(pr, GetAreaLabel(pr));
        }

        Dictionary<PullRequest, string?> pr_owner = new Dictionary<PullRequest, string?>();
        foreach (var item in pullRequests)
        {
            var pr = item.Item1;
            var area = pr_area[pr];
            if (area is null)
                continue;

            var assignee = pr.Assignees.FirstOrDefault()?.Login;
            if (assignee is not null)
            {
                var mun = await _userNameResolver.ResolveUsernameForLogin(assignee);
                if (mun is not null)
                {
                    pr_owner.Add(pr, mun);
                    continue;
                }
            }
        }

        //
        // Group by Area, then by largest count in the area, then date
        //
        var grouping = pullRequests.
            Where(l => pr_owner.ContainsKey(l.Item1)).
            GroupBy(l => pr_owner[l.Item1]).
            Select(l => new
            {
                Lead = l.Key,
                Items = l.OrderBy(ll => ll.Item2).ToList()
            }).
            OrderByDescending(id => id.Items.Count()).
            ThenBy(id => id.Items.First().Item2);

        foreach (var group in grouping)
        {
            sw.WriteLine($"<p>{WebUtility.HtmlEncode(group.Lead)}</p>");

            sw.WriteLine("<table>");
            sw.WriteLine("<thead><tr><th>Pull Request</th><th>Assignee</th><th>Area</th><th>Stale Days</th></thead>");
            sw.WriteLine("<tbody>");

            foreach (var item in group.Items)
            {
                var pr = item.Item1;
                sw.WriteLine("<tr>");
                sw.WriteLine($"<td class=\"c1\"><a href=\"{pr.HtmlUrl}\">{WebUtility.HtmlEncode(pr.Title.Trim())}</a></td>");
                sw.WriteLine($"<td class=\"c2\">{WebUtility.HtmlEncode(await _userNameResolver.ResolveUsernameForLogin(pr.Assignees.FirstOrDefault()?.Login))}</td>");
                var scope = pr_area[pr];
                sw.WriteLine($"<td class=\"c3\">{WebUtility.HtmlEncode(scope?.Substring(scope.IndexOf('-') + 1))}</td>");
                sw.WriteLine($"<td class=\"c4\">{(int)(DateTime.Today - item.Item2).TotalDays}</td>");
                sw.WriteLine("</tr>");
            }

            sw.WriteLine("</tbody>");
            sw.WriteLine("</table>");
        }

        return sw.ToString();
    }

    private static string? GetAreaLabel(PullRequest pullRequest)
    {
        string? area = FindAreaLabel("area");
        if (area is null)
        {
            Console.WriteLine($"PR {pullRequest.HtmlUrl} is missing area label");
        }

        return area;

        string? FindAreaLabel(string prefix)
        {
            string? label = null;
            foreach (var l in pullRequest.Labels)
            {
                if (!l.Name.StartsWith(prefix + "-", StringComparison.InvariantCultureIgnoreCase))
                    continue;

                if (label != null)
                {
                    Console.WriteLine($"PR {pullRequest.HtmlUrl} has multiple {prefix} labels");
                    break;
                }

                label = l.Name;
            }

            return label;
        }
    }

    private async Task<List<(PullRequest, DateTimeOffset)>> GetInactiveCommunityPRs()
    {
        var openPRsRequest = new PullRequestRequest()
        {
            State = ItemStateFilter.Open,
            //Base = "main"
        };

        var openPRs = await _client.PullRequest.GetAllForRepository(_org, _repo, openPRsRequest);
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

            var prCommits = await _client.PullRequest.Commits(_org, _repo, pr.Number);
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

    private async Task<string> GenerateHtmlTemplateForMergedPR(string login, int count)
    {
        var stars = string.Join(' ', Enumerable.Repeat("⭐", count));
        var username = await _userNameResolver.ResolveUsernameForLogin(login);
        return $"<tr><td>{username}</td><td>{stars}</td></tr>";
    }

    private async Task<IReadOnlyList<PullRequest>> GetMergedCommunityPullRequests(string org, string repo, DateTime dateTime)
    {
        const string queryDateFormat = "yyyy-MM-dd";

        var result = new List<PullRequest>();

        var searchResults = await _client.Search.SearchIssues(new SearchIssuesRequest($"is:pr repo:{org}/{repo} is:merged label:{_communityContributionLabel} created:>{dateTime.ToString(queryDateFormat)}"));
        foreach (var item in searchResults.Items)
        {
            result.Add(await _client.PullRequest.Get(org, repo, item.Number));
        }

        return result.AsReadOnly();
    }
}
