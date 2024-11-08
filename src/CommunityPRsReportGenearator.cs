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
    private const string servicingApprovedLabelName = "servicing-approved";
    private const string closedPRIcon = "🛡️";
    private const string mergedPRIcon = "⭐";
    private const string helpWantedIssueIcon = "🔰";

    private IUserNameResolver _userNameResolver;
    private int _communityPrSLAInDays;

    public CommunityPRsReportGenearator(IUserNameResolver userNameResolver, int communityPrSLAInDays)
    {
        _userNameResolver = userNameResolver;
        _communityPrSLAInDays = communityPrSLAInDays;
    }

    public async Task<string> GenerateMembersRecognitionReport(IReadOnlyList<PullRequest> completedCommunityPRs, IDictionary<string, List<Issue>> helpWantedIssuesMap, int cutoffDaysForCompletedPRs)
    {
        Dictionary<string, MemberRecognitionRecord> completedPRsByAuthors = GroupPRsByMember(completedCommunityPRs);

        List<MemberRecognitionRecord> reportData = GenerateReportData(helpWantedIssuesMap, completedPRsByAuthors);

        var result = new StringBuilder();

        result.Append($"<br /><div style='font-weight:bold'>Community help report for the last {cutoffDaysForCompletedPRs} days</div>");
        result.Append("<table><tr><th>Member</th>");
        result.Append($"<th>Number of Merged PRs ({GetInfoLabelMarkup(mergedPRIcon, "Merged PRs")})</th>");
        result.Append($"<th>Number of Closed PRs ({GetInfoLabelMarkup(closedPRIcon, "Closed PRs")})</th>");
        result.Append($"<th>Number of Issues labeled as `help wanted` ({GetInfoLabelMarkup(helpWantedIssueIcon, "Help Wanted Issues")})</th>");
        result.Append("</tr>");

        foreach (var item in reportData.OrderByDescending(i => i.PullRequests.Count))
        {
            result.AppendLine(await GenerateHtmlTemplateForMemberRecognition(item.Member, item));
        }

        result.Append("</table>");

        return result.ToString();
    }

    private static List<MemberRecognitionRecord> GenerateReportData(IDictionary<string, List<Issue>> helpWantedIssuesMap, Dictionary<string, MemberRecognitionRecord> completedPRsByAuthors)
    {
        var reportData = new List<MemberRecognitionRecord>();
        var allUsers = completedPRsByAuthors.Keys.Union(helpWantedIssuesMap.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var user in allUsers)
        {
            MemberRecognitionRecord recognition;
            if (!completedPRsByAuthors.TryGetValue(user, out recognition))
            {
                recognition = new MemberRecognitionRecord(user);
            }

            reportData.Add(recognition);

            List<Issue> convertedIssues;
            if (helpWantedIssuesMap.TryGetValue(user.ToLower(), out convertedIssues))
            {
                recognition.ConvertedHelpWantedIssues.AddRange(convertedIssues);
            }
        }

        return reportData;
    }

    private static Dictionary<string, MemberRecognitionRecord> GroupPRsByMember(IReadOnlyList<PullRequest> completedCommunityPRs)
    {
        var completedPRsByAuthors = new Dictionary<string, MemberRecognitionRecord>();

        foreach (var pr in completedCommunityPRs)
        {
            /// Ideally we would use `pr.MergedBy` information here, however this list includes PRs which weren't merged but closed,
            /// and for those PRs the `mergeBy` will be `null`. Hence, the usage of `Assignee` as per the process the ASP.NET Core repo has in place,
            /// it's the `Assignee`'s responsibility to close / merge PRs.
            User userWhoHandledThePR = pr.Assignee;
            if (pr.MergedBy is not null && !pr.Labels.Any(l => l.Name.Equals(servicingApprovedLabelName, StringComparison.OrdinalIgnoreCase)))
                userWhoHandledThePR = pr.MergedBy;

            if (userWhoHandledThePR is null)
            {
                Console.WriteLine($"Unable to find an owner for PR #{pr.Number}");
                continue;
            }

            string handledBy = userWhoHandledThePR.Login;

            if (!completedPRsByAuthors.TryGetValue(handledBy, out var recognitionRecord))
            {
                recognitionRecord = new MemberRecognitionRecord(handledBy);
                completedPRsByAuthors[handledBy] = recognitionRecord;
            }

            recognitionRecord.PullRequests.Add(pr);
        }

        return completedPRsByAuthors;
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

    private async Task<string> GenerateHtmlTemplateForMemberRecognition(string login, MemberRecognitionRecord recognition)
    {
        var starCount = recognition.PullRequests.Count(p => p.Merged);
        var closedCount = recognition.PullRequests.Count(p => !p.Merged);
        var helpWantedCount = recognition.ConvertedHelpWantedIssues.Count;

        var username = await _userNameResolver.ResolveUsernameForLogin(login);
        var result = new StringBuilder($"<tr><td>{username}</td><td>");

        if (starCount > 0)
            result.Append($"{starCount} {GetMarkupForIconWithTooltip(mergedPRIcon, "Merged PRs")}");

        result.Append("</td><td>");
        if (closedCount > 0)
        {
            if (starCount > 0)
                result.Append("<br />");

            result.Append($"{closedCount} {GetMarkupForIconWithTooltip(closedPRIcon, "Closed PRs")}");
        }

        result.Append("</td><td>");

        if (helpWantedCount > 0)
            result.Append($"{helpWantedCount} {GetMarkupForIconWithTooltip(helpWantedIssueIcon, "Help Wanted Issues")}");

        result.Append("</td></tr>");

        return result.ToString();
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

    private static string GetMarkupForIconWithTooltip(string icon, string tooltipText)
    {
        return $"<span tooltip='{tooltipText}' class='tooltip'>{icon}</span>";
    }

    private string GetInfoLabelMarkup(string icon, string info)
    {
        return $"<span>{icon}</span>";
    }
}
