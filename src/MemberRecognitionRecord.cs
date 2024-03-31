using Octokit;
using System.Collections.Generic;

namespace prmonitor;

internal class MemberRecognitionRecord
{
    public MemberRecognitionRecord(string member)
    {
        Member = member;
    }

    public string Member { get; init; }

    public List<PullRequest> PullRequests { get; set; } = new();

    public List<Issue> ConvertedHelpWantedIssues { get; set; } = new();

}
