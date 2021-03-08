using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Octokit;
using System.Linq;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Net;

namespace prmonitor {
	partial class Program {
		const string org = "dotnet";
		const string repo = "runtime";

		static void Main (string[] args)
		{
			PopulateLeadsArea ().Wait ();

			GitHubClient client = new GitHubClient (new ProductHeaderValue ("Octokit.Samples"));
			client.Credentials = CreateCredentials ();

			var request = new PullRequestRequest () {
				State = ItemStateFilter.Open
			};

			var prs = client.PullRequest.GetAllForRepository (org, repo, request).Result;

			var rl = client.GetLastApiInfo ().RateLimit;
			Console.WriteLine ($"Remaining api limit {rl.Remaining} will reset at {rl.Reset.ToLocalTime ()}");

			DateTime cutDate = DateTime.Today.AddDays (-21);

			int drafts = 0;
			int active = 0;
			var inactive = new List<(PullRequest, DateTime)> ();

			foreach (PullRequest pr in prs) {
				if (pr.Draft) {
					++drafts;
					continue;
				}

				if (IsActivePR (client, pr, cutDate, out DateTime lastActivity)) {
					++active;
					continue;
				}

				inactive.Add ((pr, lastActivity));
			}

			StringWriter sw = new StringWriter ();
			ReportInactivePRs (inactive, sw);

			var res = typeof (Program).Assembly.GetManifestResourceStream ("prmonitor.output.html.template");

			using (var input = new StreamReader (res!, Encoding.UTF8)) {
				var text = input.ReadToEnd ().Replace ("##BODY##", sw.ToString ()).Replace ("##DATE##", DateTime.Today.ToString ("dd MMMM yyyy"));
				File.WriteAllText ("../../../output.html", text);
			}

			return;
		}

		static bool IsActivePR (GitHubClient client, PullRequest pr, DateTime activityDate, out DateTime lastActivity)
		{
			// Is the latest commit date newer
			var last_commit = client.Repository.Commit.Get (org, repo, pr.Head.Sha).Result;
			var last_commit_date = last_commit.Commit.Author.Date.Date;
			if (last_commit_date > activityDate) {
				lastActivity = last_commit_date;
				return true;
			}

			// Is the latest review comment date newer
			var review = client.PullRequest.ReviewComment.GetAll (org, repo, pr.Number).Result;
			if (review.Any (l => l.CreatedAt > activityDate)) {
				lastActivity = DateTime.MaxValue;
				return true;
			}

			// Is the latest PR comment date newer
			var comments = client.Issue.Comment.GetAllForIssue (org, repo, pr.Number).Result;
			if (comments.Any (l => l.CreatedAt > activityDate)) {
				lastActivity = DateTime.MaxValue;
				return true;
			}

			lastActivity = comments.Select (l => l.CreatedAt.Date).Concat (review.Select (l => l.CreatedAt.Date)).DefaultIfEmpty (DateTime.MinValue).Max ();
			if (last_commit_date > lastActivity)
				lastActivity = last_commit_date;

			return false;
		}

		static void ReportInactivePRs (List<(PullRequest, DateTime)> pullRequests, StringWriter sw)
		{
			Dictionary<PullRequest, string?> pr_areas = new Dictionary<PullRequest, string?> ();
			foreach (var item in pullRequests) {
				var pr = item.Item1;
				pr_areas.Add (pr, GetArea (pr));
			}

			Dictionary<PullRequest, string?> pr_leads = new Dictionary<PullRequest, string?> ();
			foreach (var item in pullRequests) {
				var pr = item.Item1;
				var area = pr_areas[pr];
				if (area is null)
					continue;

				pr_leads.Add (pr, GetAreaLead (area));
			}

			//
			// Group by Area, then by largest count in the area, then date
			//
			var grouping = pullRequests.
				Where (l => pr_leads.ContainsKey (l.Item1)).
				GroupBy (l => pr_leads[l.Item1]).
				Select (l => new {
					Lead = l.Key,
					Items = l.OrderBy (ll => ll.Item2).ToList ()
				}).
				OrderByDescending (id => id.Items.Count ()).
				ThenBy (id => id.Items.First ().Item2);

			foreach (var group in grouping) {
				sw.WriteLine ($"<p>{WebUtility.HtmlEncode (group.Lead)}</p>");

				sw.WriteLine ("<table>");
				sw.WriteLine ("<thead><tr><th>Pull Request</th><th>Assignee</th><th>Area</th><th>Inactive Days</th></thead>");
				sw.WriteLine ("<tbody>");

				foreach (var item in group.Items) {
					var pr = item.Item1;
					sw.WriteLine ("<tr>");
					sw.WriteLine ($"<td class=\"c1\"><a href=\"{ pr.HtmlUrl }\">{WebUtility.HtmlEncode (pr.Title.Trim ())}</a></td>");
					sw.WriteLine ($"<td class=\"c2\">{ WebUtility.HtmlEncode (pr.Assignees.FirstOrDefault ()?.Login)}</td>");
					sw.WriteLine ($"<td class=\"c3\">{ WebUtility.HtmlEncode (pr_areas[pr]?[5..])}</td>");
					sw.WriteLine ($"<td class=\"c4\">{ (DateTime.Today - item.Item2).TotalDays}</td>");
					sw.WriteLine ("</tr>");
				}

				sw.WriteLine ("</tbody>");
				sw.WriteLine ("</table>");
			}
		}

		static string? GetArea (PullRequest pullRequest)
		{
			string? area = null;
			foreach (var l in pullRequest.Labels) {
				if (!l.Name.StartsWith ("area-", StringComparison.InvariantCultureIgnoreCase))
					continue;

				if (area != null) {
					Console.WriteLine ($"PR {pullRequest.HtmlUrl} has multiple area labels");
					continue;
				}

				area = l.Name;
			}

			if (area == null) {
				Console.WriteLine ($"PR {pullRequest.HtmlUrl} has no area label");
			}

			return area;
		}

		static Dictionary<string, string> leadsCache = new Dictionary<string, string> ();
		static Dictionary<string, string> leadsNames = new Dictionary<string, string> () {
			{ "@agocke", "Andy Gocke" },
			{ "@SamMonoRT", "Sam Patel" },
			{ "@ericstj", "Eric St. John" },
			{ "@karelz", "Karel Zikmund" },
			{ "@steveisok", "Steve Pfister" },
			{ "@lewing", "Larry Ewing" },
			{ "@jeffhandley", "Jeff Handley" },
			{ "@JulieLeeMSFT", "Julie Lee" },
			{ "@jeffschwMSFT", "Jeff Schwartz" },
			{ "@tommcdon", "Tom McDonald" },
			{ "@mangod9", "Manish Godse" },
			{ "@dleeapho", "Dan Leeaphon" },
			{ "@HongGit", "Hong Li" }
		};

		static async Task PopulateLeadsArea ()
		{
			var http = new HttpClient ();
			var data = await http.GetStringAsync ("https://raw.githubusercontent.com/dotnet/runtime/master/docs/area-owners.md");
			bool first = true;
			foreach (var line in data.Split ("| area-")) {
				if (first) {
					first = false;
					continue;
				}

				var area_data = line.Split ('|');
				if (area_data.Length < 2) {
					Console.WriteLine ("Unexpected leads format");
					continue;
				}

				var area = "area-" + area_data[0].Trim ();
				var lead = area_data[1].Trim ();
				if (leadsCache.ContainsKey (area)) {
					Console.WriteLine ($"Duplicate area lead for '{area}'");
					continue;
				}

				leadsCache.Add (area, lead);
			}
		}

		static string GetAreaLead (string area)
		{
			if (!leadsCache.TryGetValue (area, out var lead)) {
				Console.WriteLine ("Missing lead for " + area);
				return "Unknown";
			}

			if (leadsNames.TryGetValue (lead, out var name))
				return name;

			Console.WriteLine ("Missing lead alias mapping for " + lead);
			return "??";
		}
	}
}
