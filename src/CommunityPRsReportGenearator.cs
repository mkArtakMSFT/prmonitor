using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace prmonitor;

internal class CommunityPRsReportGenearator
{
    private IUserNameResolver _userNameResolver;
    private int _communityPrSLAInDays;

    public CommunityPRsReportGenearator(IUserNameResolver userNameResolver, int communityPrSLAInDays)
    {
        _userNameResolver = userNameResolver;
        _communityPrSLAInDays = communityPrSLAInDays;
    }

    public async Task<string> GenerateMergedPullRequestsReport(IReadOnlyList<PullRequest> mergedCommunityPRs, int cutoffDaysForMergedPRs)
    {
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
        result.Append($"<br /><div style='font-weight:bold'>Community PRs merged during last {cutoffDaysForMergedPRs} days</div>");
        result.Append("<table><tr><th>Merged By</th><th>Number of PRs merged</th></tr>");
        foreach (var item in mergedPRsByAuthors.OrderByDescending(i => i.Value))
        {
            result.AppendLine(await GenerateHtmlTemplateForMergedPR(item.Key, item.Value));
        }

        result.Append("</table>");

        return result.ToString();
    }

    public async Task<string> GenerateInactiveCommunityPRsReportInternal(List<(PullRequest, DateTimeOffset)> pullRequests)
    {
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
                await AppendPRInfo(sw, pr_area, item.Item1, item.Item2);
            }

            sw.WriteLine("</tbody>");
            sw.WriteLine("</table>");
        }

        return sw.ToString();
    }

    private async Task AppendPRInfo(StringWriter sw, Dictionary<PullRequest, string> pr_area, PullRequest pr, DateTimeOffset daysSincePRCreated)
    {
        sw.WriteLine("<tr>");

        sw.WriteLine($"<td class=\"c1\"><a href=\"{pr.HtmlUrl}\">{WebUtility.HtmlEncode(pr.Title.Trim())}</a></td>");
        sw.WriteLine($"<td class=\"c2\">{WebUtility.HtmlEncode(await _userNameResolver.ResolveUsernameForLogin(pr.Assignees.FirstOrDefault()?.Login))}</td>");
        var scope = pr_area[pr];
        sw.WriteLine($"<td class=\"c3\">{WebUtility.HtmlEncode(scope?.Substring(scope.IndexOf('-') + 1))}</td>");
        AppendPrDaysCellDataToReport(sw, daysSincePRCreated);

        sw.WriteLine("</tr>");
    }

    private void AppendPrDaysCellDataToReport(StringWriter sw, DateTimeOffset daysSincePRCreated)
    {
        var staleDays = (int)(DateTime.Today - daysSincePRCreated).TotalDays;
        var cellContent = staleDays.ToString();
        if (staleDays >= _communityPrSLAInDays)
            cellContent += " ⚠️";

        sw.Write("<td class=\"c4");
        if (staleDays >= _communityPrSLAInDays)
            sw.Write(" prDaysBeyondSLA");
        sw.Write("\">");
        sw.Write(WebUtility.HtmlEncode(cellContent));
        sw.WriteLine("</td>");
    }

    private async Task<string> GenerateHtmlTemplateForMergedPR(string login, int count)
    {
        var stars = string.Join(' ', Enumerable.Repeat("⭐", count));
        var username = await _userNameResolver.ResolveUsernameForLogin(login);
        return $"<tr><td>{username}</td><td>{stars}</td></tr>";
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
}
