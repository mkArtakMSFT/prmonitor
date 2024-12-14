# PR Monitor
PRMonitor is a utility for generating a report based on community-submitted PRs to the dotnet/aspnetcore repo.

## Report
The report is generated as an `.html` file and includes the following information:
- Information about number of merged and closed PRs per team member
- Information about number of issues that got `help-wanted` label added
- Community PR report

The Community PR report, represents the list of PRs in a dotnet/aspnetcore repo, which meet the following criteria:
- PRs are open
- have a `community-contribution` label
- Are not in `draft` state
- The last commit in the PR is older than 14 days.

## How to run the tool
After building the tool, run the executable by passing in a GitHub Personal Access Token as the only parameter.
That token will need to have a read access to the repo the reported to be generated for (dotnet/aspnetcore currently).
