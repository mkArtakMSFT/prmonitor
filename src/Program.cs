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
        static void Main (string[] args)
        {
            PopulateLeadsArea ().Wait ();

            GitHubClient client = new GitHubClient (new ProductHeaderValue ("Octokit.Samples"));
            client.Credentials = CreateCredentials ();

            var request = new PullRequestRequest () {
                State = ItemStateFilter.Open
            };

            var prs = client.PullRequest.GetAllForRepository ("dotnet", "runtime", request).Result;

            DateTime cutDate = DateTime.Today.AddDays (-30);

            int drafts = 0;
            int active = 0;
            var inactive = new List<PullRequest> ();

            foreach (PullRequest pr in prs) {
                if (pr.Draft) {
                    ++drafts;
                    continue;
                }

                if (pr.UpdatedAt > cutDate) {
                    ++active;
                    continue;
                }

                inactive.Add (pr);
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

        static void ReportInactivePRs (List<PullRequest> pullRequests, StringWriter sw)
        {
            Dictionary<PullRequest, string?> pr_areas = new Dictionary<PullRequest, string?> ();
            foreach (var pr in pullRequests) {
                pr_areas.Add (pr, GetArea (pr));
            }

            Dictionary<PullRequest, string?> pr_leads = new Dictionary<PullRequest, string?> ();
            foreach (var pr in pullRequests) {
                var area = pr_areas[pr];
                if (area is null)
                    continue;

                pr_leads.Add (pr, GetAreaLead (area));
            }

            //
            // Group by Area, then by largest count in the area, then date
            //
            var grouping = pullRequests.
                Where (l => pr_leads.ContainsKey (l)).
                GroupBy (l => pr_leads[l]).
                Select (l => new {
                    Lead = l.Key,
                    Items = l.OrderBy (ll => ll.UpdatedAt).ToList ()
                }).
                OrderByDescending (id => id.Items.Count ()).
                ThenBy (id => id.Items.First ().UpdatedAt);

            foreach (var pr in grouping) {
                sw.WriteLine ($"<p>{WebUtility.HtmlEncode(pr.Lead)}</p>");

                sw.WriteLine ("<table>");
                sw.WriteLine ("<thead><tr><th>Pull Request</th><th>Assignee</th><th>Area</th><th>Inactive Days</th></thead>");
                sw.WriteLine ("<tbody>");

                foreach (var item in pr.Items) {
                    sw.WriteLine ("<tr>");
                    sw.WriteLine ($"<td class=\"c1\"><a href=\"{ item.HtmlUrl }\">{WebUtility.HtmlEncode(item.Title.Trim ())}</a></td>");
                    sw.WriteLine ($"<td class=\"c2\">{ WebUtility.HtmlEncode(item.Assignees.FirstOrDefault ()?.Login)}</td>");
                    sw.WriteLine ($"<td class=\"c3\">{ WebUtility.HtmlEncode(pr_areas[item]?[5..])}</td>");
                    sw.WriteLine ($"<td class=\"c4\">{ (DateTime.Today - item.UpdatedAt.Date).TotalDays}</td>");
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
