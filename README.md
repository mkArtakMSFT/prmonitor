# PR Monitor

This project started as a clone of the code in https://github.com/marek-safar/prmonitor repo.
Some features didn't make sense to have , so those have been removed, as well as few other convenience things have been added.

When run, the tool generates a report in an `.html` format, showing the list of PRs in a dotnet/aspnetcore repo, which meet the following criteria:
- PRs are open
- have a `community-contribution` label
- Are not in `draft` state
- The last commit in the PR is older than 14 days.

## How to run the tool
After building the tool, run the executable by passing in a GitHub Personal Access Token as the only parameter.
That token will need to have a read access to the repo the reported to be generated for (dotnet/aspnetcore currently).
