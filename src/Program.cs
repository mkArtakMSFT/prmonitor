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
    private const string communityContributionLabel = "community-contribution";
    private const string template_body = "##BODY##";
    private const string template_recognitions = "##RECOGNITIONS##";
    private const int cutoffDaysForMergedPRs = 7;
    private const int cutoffDaysForInactiveCommunityPRs = 14;

    /// <summary>
    /// Pass in the PAT for accessing the github repo as the first argument to the program when called
    /// </summary>
    /// <param name="args"></param>
    static async Task Main(string[] args)
    {
        GitHubClient client = new GitHubClient(new ProductHeaderValue("Octokit.Samples"));
        client.Credentials = new Credentials(args[0]);

        var userNameResolver = new UserNameResolver(client);

        var reportGenerator = new CommunityPRsReportGenerator(client, userNameResolver, org, repo, cutoffDaysForMergedPRs, cutoffDaysForInactiveCommunityPRs, communityContributionLabel);
        var mergedPRsReport = await reportGenerator.GenerateReportForMergedPullRequests();

        var rl = client.GetLastApiInfo().RateLimit;
        Console.WriteLine($"Remaining GH api limit {rl.Remaining} will reset at {rl.Reset.ToLocalTime()}");

        string inactiveCommunityPRsReport = await reportGenerator.GetInactiveCommunityPRsReport();

        var res = typeof(Program).Assembly.GetManifestResourceStream("prmonitor.output.html.template");

        using var input = new StreamReader(res!, Encoding.UTF8);
        var text = input.ReadToEnd().Replace(template_body, inactiveCommunityPRsReport).Replace("##DATE##", DateTime.Today.ToString("dd MMMM yyyy"));
        text = text.Replace(template_recognitions, mergedPRsReport);

        File.WriteAllText("output.html", text);

        return;
    }
}