using Newtonsoft.Json;
using NGit;
using NGit.Api;
using NGit.Storage.File;
using NGit.Transport;
using RestSharp;
using Sharpen;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace SchemaTracker
{
    public partial class SchemaService
    {
        public enum UpdateType
        {
            Schema,
            ClientSchema,
            SchemaClass
        }

        private static readonly ILog Log = LogManager.GetLog(typeof(SchemaService));
        private const string ApiDown = "Steam Web API is Currently Down";
        private const string SteamEconTemplate = "https://github.com/SchemaTracker/SteamEconTemplate.git";

        public HashSet<EconApp> Apps { get; private set; }

        public string ApiKey { get; private set; }

        public string Language { get; private set; }

        internal string CacheDirectory { get; set; }

        internal string SchemaDirectory { get; set; }

        public int CheckInterval { get; private set; }

        internal Dictionary<int, EconApp> AppMap { get; set; }

        internal Dictionary<string, KeyValuePair<EconApp, UpdateType>> ReverseAppMap { get; set; }

        internal bool NeedsReInit { get; private set; }

        private bool IsFatal { get; set; }

        private bool UseTempPath { get; set; }

        private List<EconApp> ClientSchemaUrls { get; set; }

        private GitInfo GitInfo { get; set; }

        private readonly BackgroundWorker worker;

        public SchemaService(string apiKey, GitInfo gitInfo, HashSet<EconApp> apps = null, int checkInterval = 900000, string language = "en_US", bool useTempPath = true)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Log.Warn("Missing ApiKey");
                throw new ArgumentNullException("apiKey");
            }
            if (gitInfo == null)
            {
                Log.Warn("Missing GitInfo");
                throw new ArgumentNullException("gitInfo");
            }
            if (apps == null || !apps.Any())
            {
                apps = Helper.DefaultAppIds;
            }

            this.ApiKey = apiKey;
            this.GitInfo = gitInfo;
            this.Apps = apps;
            this.Language = language;
            this.CheckInterval = checkInterval;
            this.UseTempPath = useTempPath;

            worker = new BackgroundWorker() { WorkerSupportsCancellation = true };
            worker.DoWork += CheckSchemas;
            worker.RunWorkerCompleted += WorkerCompleted;
            try
            {
                CacheDirectory = Helper.CombinePaths(UseTempPath, gitInfo.LocalRepoName, "cache");
                if (!Directory.Exists(CacheDirectory)) { Directory.CreateDirectory(CacheDirectory); }
                SchemaDirectory = Helper.CombinePaths(UseTempPath, gitInfo.LocalRepoName, "SteamEcon", "Schema");
                if (!Directory.Exists(SchemaDirectory)) { Directory.CreateDirectory(SchemaDirectory); }
            }
            catch (Exception ex)
            {
                Log.Warn("Couldn't create directories");
                Log.Error(ex);
            }

            InitApps();
        }

        #region Background Worker Methods

        private void CheckSchemas(object sender, DoWorkEventArgs doWorkEventArgs)
        {
            Log.Info("BackgroundWorker Running");
            while (!worker.CancellationPending)
            {
                Clone(GitInfo);
                DownloadSchemas();
                CommitAnyChanges(GitInfo);
                Log.Info("Waiting for next check");
                Thread.Sleep(CheckInterval);
            }
        }

        public void Start()
        {
            if (!IsFatal)
            {
                Log.Info("BackgroundWorker Starting");
                if (NeedsReInit)
                {
                    Log.Info("Reinitializing Apps");
                    InitApps();
                }
                worker.RunWorkerAsync();
            }
            else
            {
                Log.Warn("BackgroundWorker can not be started");
            }
        }

        public void Stop()
        {
            Log.Info("BackgroundWorker Stopping");
            worker.CancelAsync();
        }

        private void WorkerCompleted(object sender, RunWorkerCompletedEventArgs runWorkerCompletedEventArgs)
        {
            NeedsReInit = true;
            Log.Info("BackgroundWorker Finished");
        }

        #endregion Background Worker Methods

        private void InitApps()
        {
            IsFatal = false;
            AppMap = new Dictionary<int, EconApp>();
            ReverseAppMap = new Dictionary<string, KeyValuePair<EconApp, UpdateType>>();
            ClientSchemaUrls = new List<EconApp>();
            foreach (var app in Apps)
            {
                app.SchemaUrl = string.Format("http://api.steampowered.com/IEconItems_{0}/GetSchema/v1/?key={1}&language={2}",
                    app.Id, ApiKey, Language);
                try
                {
                    DateTime modifiedLast = File.Exists(Path.Combine(CacheDirectory, app.SchemaFileName))
                        ? File.GetLastWriteTime(Path.Combine(CacheDirectory, app.SchemaFileName))
                        : default(DateTime);
                    bool exists = File.Exists(Path.Combine(CacheDirectory, app.SchemaFileName));
                    if (exists)
                    {
                        dynamic json =
                            JsonConvert.DeserializeObject<ExpandoObject>(
                                File.ReadAllText(Path.Combine(CacheDirectory, app.SchemaFileName)));
                        app.ClientSchemaUrl = json.items_game_url;
                    }
                    app.LastModified = modifiedLast;
                    ReverseAppMap.Add(app.SchemaFileName, new KeyValuePair<EconApp, UpdateType>(app, UpdateType.Schema));
                    ReverseAppMap.Add(app.ClientSchemaFileName, new KeyValuePair<EconApp, UpdateType>(app, UpdateType.ClientSchema));
                    ReverseAppMap.Add(app.SchemaClassFileName, new KeyValuePair<EconApp, UpdateType>(app, UpdateType.SchemaClass));
                    AppMap.Add(app.Id, app);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    IsFatal = true;
                }
            }
        }

        #region Processing Methods

        private bool ProcessStatusCode(IRestResponse response, EconApp app)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.NotModified:
                    break;

                case HttpStatusCode.OK:
                    Log.Info("Update {0}", app.Name);
                    return true;

                case HttpStatusCode.ServiceUnavailable:
                    Log.Warn(ApiDown);
                    break;

                case HttpStatusCode.InternalServerError:
                    Log.Warn(ApiDown);
                    break;

                default:
                    Log.Warn("Unexpected status {0} {1}", response.StatusCode, response.StatusDescription);
                    break;
            }
            return false;
        }

        private bool ProcessSchemaResponse(string response, EconApp app)
        {
            bool newClientSchema = false;
            try
            {
                dynamic json = JsonConvert.DeserializeObject<ExpandoObject>(response);

                if (json != null && json.result != null && json.result.status == 1)
                {
                    if (json.result.items_game_url != null)
                    {
                        var url = json.result.items_game_url;
                        if (url != app.ClientSchemaUrl)
                        {
                            app.ClientSchemaUrl = url;
                            newClientSchema = true;
                            ClientSchemaUrls.Add(app);
                        }
                    }
                    string resultonly = JsonConvert.SerializeObject(json.result, Formatting.Indented);
                    File.WriteAllText(Path.Combine(CacheDirectory, app.SchemaFileName), resultonly);
                    app.LastModified = File.GetLastWriteTime(Path.Combine(CacheDirectory, app.SchemaFileName));
                    GeneratorService.GenerateClass(app, SchemaDirectory, resultonly);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Error processing response from {0}", app);
                Log.Error(ex);
            }

            return newClientSchema;
        }

        private bool ProcessClientSchemaResponse(string response, EconApp app)
        {
            try
            {
                if (response != null)
                {
                    File.WriteAllText(Path.Combine(CacheDirectory, app.ClientSchemaFileName), response);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            return false;
        }

        private IEnumerable<string> CheckChanges(Git repo, IEnumerable<string> files, string changeText)
        {
            var messages = new List<string>();
            var filesToAdd = new List<string>();
            var updates = new Dictionary<UpdateType, List<string>>();
            updates.Add(UpdateType.Schema, new List<string>());
            updates.Add(UpdateType.ClientSchema, new List<string>());
            updates.Add(UpdateType.SchemaClass, new List<string>());
            foreach (var file in files)
            {
                KeyValuePair<EconApp, UpdateType> appInfo;
                if (ReverseAppMap.TryGetValue(Path.GetFileName(file), out appInfo))
                {
                    switch (appInfo.Value)
                    {
                        case UpdateType.Schema:
                            updates[UpdateType.Schema].Add(appInfo.Key.Name);
                            filesToAdd.Add(file);
                            break;

                        case UpdateType.ClientSchema:
                            updates[UpdateType.ClientSchema].Add(appInfo.Key.Name);
                            filesToAdd.Add(file);
                            break;

                        case UpdateType.SchemaClass:
                            updates[UpdateType.SchemaClass].Add(appInfo.Key.Name);
                            filesToAdd.Add(file);
                            break;
                    }
                }
            }
            foreach (var updateType in updates.Keys)
            {
                if (updates[updateType].Count > 0)
                {
                    string csv = String.Join(", ", updates[updateType].Select(x => x).ToArray());
                    messages.Add(string.Format("{0} {1} files for : {2}", changeText, updateType, csv));
                }
            }
            foreach (var f in filesToAdd)
            {
                repo.Add().AddFilepattern(f).Call();
            }
            return messages;
        }

        private bool ProcessGitStatus(Git repo, out string commitMessage)
        {
            commitMessage = "";
            var status = repo.Status().Call();
            var untracked = status.GetUntracked();
            var modified = status.GetModified();
            var messages = new List<string>();
            bool isDirty = false;
            if (untracked.Count > 0)
            {
                var changes = CheckChanges(repo, untracked, "Added");
                if (changes.Any())
                {
                    isDirty = true;
                    messages.AddRange(changes);
                }
            }
            if (modified.Count > 0)
            {
                var changes = CheckChanges(repo, modified, "Updated");
                if (changes.Any())
                {
                    isDirty = true;
                    messages.AddRange(changes);
                }
            }
            if (isDirty)
            {
                commitMessage = String.Join(Environment.NewLine, messages.Select(x => x).ToArray());
            }
            return isDirty;
        }

        #endregion Processing Methods

        internal void DownloadSchemas()
        {
            var client = new RestClient();
            client.UserAgent = "SchemaTracker";
            foreach (var app in Apps)
            {
                var request = new RestRequest(app.SchemaUrl, Method.GET);
                request.AddHeader("If-Modified-Since", app.LastModified.ToString());
                var response = client.Execute(request);
                if (ProcessStatusCode(response, app) && ProcessSchemaResponse(response.Content, app))
                {
                    request = new RestRequest(app.ClientSchemaUrl, Method.GET);
                    response = client.Execute(request);
                    if (ProcessStatusCode(response, app))
                    {
                        ProcessClientSchemaResponse(response.Content, app);
                    }
                }
            }
        }

        public void NewRepo(GitInfo gitInfo)
        {
            try
            {
                Log.Info("Creating New Repo");
                var creds = new UsernamePasswordCredentialsProvider(gitInfo.UserName, gitInfo.Password);
                Git.CloneRepository()
                    .SetURI(SteamEconTemplate)
                    .SetDirectory(Helper.CombinePaths(UseTempPath, gitInfo.LocalRepoName))
                    .Call();
                Git newRepo = Git.Open(Helper.CombinePaths(UseTempPath, gitInfo.LocalRepoName));
                StoredConfig config = newRepo.GetRepository().GetConfig();
                var remote = new RemoteConfig(config, "origin");
                remote.RemoveURI(new URIish(SteamEconTemplate));
                config.SetString("remote", "origin", "fetch", "+refs/*:refs/*");
                remote.AddURI(new URIish(gitInfo.RemoteRepoUrl));
                remote.Update(config);
                config.Save();
                newRepo.Push().SetCredentialsProvider(creds).Call();
                newRepo.GetRepository().Close();
                newRepo.GetRepository().ObjectDatabase.Close();
                newRepo = null;
                Log.Info("Done Creating New repo");
            }
            catch (GitException gex)
            {
                Log.Error(gex);
            }
        }

        internal void Clone(GitInfo gitInfo)
        {
            var creds = new UsernamePasswordCredentialsProvider(gitInfo.UserName, gitInfo.Password);
            if (!Directory.Exists(Helper.CombinePaths(UseTempPath, gitInfo.LocalRepoName, ".git")))
            {
                Log.Info("Cloning repo from {0}", GitInfo.RemoteRepoUrl);
                try
                {
                    Git.CloneRepository()
                        .SetURI(gitInfo.RemoteRepoUrl)
                        .SetDirectory(Helper.CombinePaths(UseTempPath, gitInfo.LocalRepoName))
                        .SetCredentialsProvider(creds)
                        .Call();
                }
                catch (GitException gex)
                {
                    Log.Error(gex);
                }
            }
        }

        internal void CommitAnyChanges(GitInfo gitInfo)
        {
            var creds = new UsernamePasswordCredentialsProvider(gitInfo.UserName, gitInfo.Password);
            try
            {
                Git repo = Git.Open(gitInfo.LocalRepoName);
                string message = string.Empty;
                if (ProcessGitStatus(repo, out message))
                {
                    Log.Info("Committing");
                    var committer = new PersonIdent(gitInfo.UserName, gitInfo.Email);
                    repo.Commit().SetMessage(message).SetAuthor(committer).SetCommitter(committer).Call();

                    Log.Info("Fetch, Rebase, Push");

                    repo.Fetch().Call();
                    repo.Rebase().SetUpstream("FETCH_HEAD").Call();
                    repo.Push().SetCredentialsProvider(creds).Call();
                }
                repo.GetRepository().Close();
                repo.GetRepository().ObjectDatabase.Close();
                repo = null;
            }
            catch (GitException gex)
            {
                Log.Error(gex);
            }
        }
    }
}