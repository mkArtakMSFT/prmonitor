using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace prmonitor;

partial class Program
{
    const string org = "dotnet";
    const string repo = "aspnetcore";
    private static Dictionary<string, string> msUserNamesCache = new Dictionary<string, string>();

    /// <summary>
    /// Pass in the PAT for accessing the github repo as the first argument to the program when called
    /// </summary>
    /// <param name="args"></param>
    static async Task Main(string[] args)
    {
        //PopulateLeadsArea().Wait();

        GitHubClient client = new GitHubClient(new ProductHeaderValue("Octokit.Samples"));
        client.Credentials = new Credentials(args[0]);

        var request = new PullRequestRequest()
        {
            State = ItemStateFilter.Open,
            //Base = "main"
        };

        var prs = client.PullRequest.GetAllForRepository(org, repo, request).Result;

        var rl = client.GetLastApiInfo().RateLimit;
        Console.WriteLine($"Remaining GH api limit {rl.Remaining} will reset at {rl.Reset.ToLocalTime()}");

        DateTimeOffset cutDate = DateTimeOffset.Now.AddDays(-14);

        var inactivePrsList = new List<(PullRequest, DateTimeOffset)>();

        foreach (PullRequest pr in prs)
        {
            // Ignore non community contribution PRs
            if (!pr.Labels.Any(l => l.Name == "community-contribution"))
                continue;

            // Ignore those PRs which are pending author input
            if (pr.Labels.Any(l => l.Name == "pr: pending author input"))
                continue;

            // Ignore draft PRs
            if (pr.Draft)
                continue;

            if (pr.CreatedAt > cutDate)
                continue;

            var prCommits = await client.PullRequest.Commits(org, repo, pr.Number);
            var lastCommitDate = prCommits.Last().Commit.Author.Date;
            if (lastCommitDate > cutDate)
            {
                // There was a recent commit on this PR, so not flagging as `stale
                continue;
            }

            inactivePrsList.Add((pr, lastCommitDate));
        }

        using StringWriter sw = new StringWriter();
        ReportInactivePRs(inactivePrsList, sw, client);

        var res = typeof(Program).Assembly.GetManifestResourceStream("prmonitor.output.html.template");

        using var input = new StreamReader(res!, Encoding.UTF8);
        var text = input.ReadToEnd().Replace("##BODY##", sw.ToString()).Replace("##DATE##", DateTime.Today.ToString("dd MMMM yyyy"));
        File.WriteAllText("output.html", text);

        return;
    }

    static void ReportInactivePRs(List<(PullRequest, DateTimeOffset)> pullRequests, StringWriter sw, GitHubClient client)
    {
        Dictionary<PullRequest, string?> pr_area = new Dictionary<PullRequest, string?>();
        foreach (var item in pullRequests)
        {
            var pr = item.Item1;
            pr_area.Add(pr, GetArea(pr));
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
                var mun = GetMicrosoftUserName(assignee, client);
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
                sw.WriteLine($"<td class=\"c2\">{WebUtility.HtmlEncode(GetMicrosoftUserName(pr.Assignees.FirstOrDefault()?.Login, client))}</td>");
                var scope = pr_area[pr];
                sw.WriteLine($"<td class=\"c3\">{WebUtility.HtmlEncode(scope?.Substring(scope.IndexOf('-') + 1))}</td>");
                sw.WriteLine($"<td class=\"c4\">{(int)(DateTime.Today - item.Item2).TotalDays}</td>");
                sw.WriteLine("</tr>");
            }

            sw.WriteLine("</tbody>");
            sw.WriteLine("</table>");
        }
    }

    static string? GetArea(PullRequest pullRequest)
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

    static string? GetMicrosoftUserName(string? login, GitHubClient client)
    {
        if (login is null)
            return null;

        if (msUserNamesCache.TryGetValue(login, out var name))
            return name;

        var un = client.User.Get(login).Result;
        msUserNamesCache.Add(login, un.Name);
        return un.Name;
    }
}