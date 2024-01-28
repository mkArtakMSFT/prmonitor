using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
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
    private const int communityPrSLAInDays = 60;

    /// <summary>
    /// Pass in the PAT for accessing the github repo as the first argument to the program when called
    /// </summary>
    /// <param name="args"></param>
    static async Task Main(string[] args)
    {
        GitHubClient client = new GitHubClient(new ProductHeaderValue("Octokit.Samples"));
        client.Credentials = new Credentials(args[0]);

        var userNameResolver = new UserNameResolver(client);

        var reportDataRetriever = new CommunityPRsReportDataRetriever(client, org, repo, cutoffDaysForInactiveCommunityPRs, communityContributionLabel);
        var reportGenerator = new CommunityPRsReportGenearator(userNameResolver, communityPrSLAInDays);

        string mergedPRsReport = await GeneratedMergedPRsReport(reportDataRetriever, reportGenerator);

#if DEBUG
        var rl = client.GetLastApiInfo().RateLimit;
        Console.WriteLine($"Remaining GH api limit {rl.Remaining} will reset at {rl.Reset.ToLocalTime()}");
#endif

        var inactiveCommunityPRsReport = await GetInactiveCommunityPRsReport(reportDataRetriever, reportGenerator);

        var res = typeof(Program).Assembly.GetManifestResourceStream("prmonitor.output.html.template");

        using var input = new StreamReader(res!, Encoding.UTF8);
        var text = input.ReadToEnd().Replace(template_body, inactiveCommunityPRsReport).Replace("##DATE##", DateTime.Today.ToString("dd MMMM yyyy"));
        text = text.Replace(template_recognitions, mergedPRsReport);

        File.WriteAllText("output.html", text);

        return;
    }

    private static async Task<string> GeneratedMergedPRsReport(CommunityPRsReportDataRetriever reportDataRetriever, CommunityPRsReportGenearator reportGenerator)
    {
        var mergedPRsReport = string.Empty;
        var mergedCommunityPRs = await reportDataRetriever.GetMergedCommunityPullRequests(DateTime.Now.AddDays(-cutoffDaysForMergedPRs));
        if (mergedCommunityPRs.Count > 0)
        {
            mergedPRsReport = await reportGenerator.GenerateMergedPullRequestsReport(mergedCommunityPRs, cutoffDaysForMergedPRs);
        }

        return mergedPRsReport;
    }

    private static async Task<string> GetInactiveCommunityPRsReport(CommunityPRsReportDataRetriever reportDataRetriever, CommunityPRsReportGenearator reportGenerator)
    {
        List<(PullRequest, DateTimeOffset)> pullRequests = await reportDataRetriever.GetInactiveCommunityPRs();

        return await reportGenerator.GenerateInactiveCommunityPRsReportInternal(pullRequests);
    }
}