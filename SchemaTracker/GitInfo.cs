namespace SchemaTracker
{
    public class GitInfo
    {
        public string UserName { get; private set; }

        public string Password { get; private set; }

        public string Email { get; private set; }

        public string RemoteRepoUrl { get; private set; }

        public string LocalRepoName { get; private set; }

        public GitInfo(string userName, string password, string email, string remoteRepoUrl, string localRepoName)
        {
            this.UserName = userName;
            this.Password = password;
            this.Email = email;
            this.RemoteRepoUrl = remoteRepoUrl;
            this.LocalRepoName = localRepoName;
        }
    }
}