namespace Hmp.Devops.Tools.EnvironmentRemover.Tests
{
    /// <summary>
    /// Constants and helpers for test data payloads.
    /// </summary>
    public static class TestData
    {
        public const string ValidCompletedPullRequestPayload = """
        {
            "eventType": "git.pullrequest.updated",
            "resource": {
                "pullRequestId": "123",
                "status": "completed",
                "repository": { "name": "TestRepo" },
                "closedBy": { "displayName": "John Doe" },
                "sourceRefName": "refs/heads/feature/test",
                "targetRefName": "refs/heads/main"
            }
        }
        """;

        public const string ValidAbandonedPullRequestPayload = """
        {
            "eventType": "git.pullrequest.updated",
            "resource": {
                "pullRequestId": "456",
                "status": "abandoned",
                "repository": { "name": "TestRepo" },
                "closedBy": { "displayName": "Jane Doe" },
                "sourceRefName": "refs/heads/bugfix/issue",
                "targetRefName": "refs/heads/main"
            }
        }
        """;

        public const string ActivePullRequestPayload = """
        {
            "eventType": "git.pullrequest.updated",
            "resource": {
                "pullRequestId": "789",
                "status": "active",
                "repository": { "name": "TestRepo" },
                "closedBy": { "displayName": "" },
                "sourceRefName": "refs/heads/feature",
                "targetRefName": "refs/heads/main"
            }
        }
        """;

        public const string MergedPullRequestPayload = """
        {
            "eventType": "git.pullrequest.merged",
            "resource": {
                "pullRequestId": "999",
                "status": "completed",
                "repository": { "name": "TestRepo" },
                "closedBy": { "displayName": "Merge Bot" },
                "sourceRefName": "refs/heads/feature",
                "targetRefName": "refs/heads/main"
            }
        }
        """;

        public const string UnsupportedEventTypePayload = """
        {
            "eventType": "git.push",
            "resource": {}
        }
        """;

        public const string MissingFieldsPayload = """
        {
            "eventType": "git.pullrequest.updated",
            "resource": {
                "pullRequestId": "123"
            }
        }
        """;
    }
}
