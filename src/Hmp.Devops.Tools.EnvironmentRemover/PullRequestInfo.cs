namespace Hmp.Devops.Tools.EnvironmentRemover
{
    public record PullRequestInfo(
        string RepositoryName,
        string PullRequestId,
        string Status,
        string ClosedBy,
        string SourceBranch,
        string TargetBranch)
    {
        public bool Closing => Status == "completed" || Status == "abandoned";
    }
}
