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
    private const int cutoffDaysForCompletedPRs = 7;
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

        var helpWantedIssuesRetriever = new HelpWantedIssuesDataRetriever(client, org, repo);

        var reportGenerator = new CommunityPRsReportGenearator(userNameResolver, communityPrSLAInDays);

        string recognitionReport = await GenerateMemberRecognitionReport(reportDataRetriever, helpWantedIssuesRetriever, reportGenerator);

#if DEBUG
        var rl = client.GetLastApiInfo().RateLimit;
        Console.WriteLine($"Remaining GH api limit {rl.Remaining} will reset at {rl.Reset.ToLocalTime()}");
#endif

        var inactiveCommunityPRsReport = await GetInactiveCommunityPRsReport(reportDataRetriever, reportGenerator);

        var res = typeof(Program).Assembly.GetManifestResourceStream("prmonitor.output.html.template");

        using var input = new StreamReader(res!, Encoding.UTF8);
        var text = await input.ReadToEndAsync();
        var builder = new StringBuilder(text);
        builder.Replace(template_body, inactiveCommunityPRsReport)
            .Replace("##DATE##", DateTime.Today.ToString("dd MMMM yyyy"))
            .Replace(template_recognitions, recognitionReport);

        await File.WriteAllTextAsync("output.html", builder.ToString());

        return;
    }

    private static async Task<string> GenerateMemberRecognitionReport(CommunityPRsReportDataRetriever reportDataRetriever, HelpWantedIssuesDataRetriever helpWantedIssuesRetriever, CommunityPRsReportGenearator reportGenerator)
    {
        var mergedPRsReport = string.Empty;
        var completedCommunityPRs = await reportDataRetriever.GetCompletedCommunityPullRequests(DateTime.Now.AddDays(-cutoffDaysForCompletedPRs));
        var helpWantedIssues = await helpWantedIssuesRetriever.RetrieveHelpWantedIssuesConvertedSinceAsync(DateTimeOffset.UtcNow.AddDays(-cutoffDaysForCompletedPRs));

        if (completedCommunityPRs.Count > 0 || helpWantedIssues.Count > 0)
        {
            mergedPRsReport = await reportGenerator.GenerateMembersRecognitionReport(completedCommunityPRs, helpWantedIssues, cutoffDaysForCompletedPRs);
        }

        return mergedPRsReport;
    }

    private static async Task<string> GetInactiveCommunityPRsReport(CommunityPRsReportDataRetriever reportDataRetriever, CommunityPRsReportGenearator reportGenerator)
    {
        var pullRequests = await reportDataRetriever.GetInactiveCommunityPRs();

        return await reportGenerator.GenerateInactiveCommunityPRsReportInternal(pullRequests);
    }
}