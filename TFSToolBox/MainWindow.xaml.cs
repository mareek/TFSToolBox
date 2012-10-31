using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using Microsoft.TeamFoundation.VersionControl.Common;
using System.IO;
using System.Security.Principal;
using Microsoft.TeamFoundation.Build.Client;

namespace TFSToolBox
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private TfsTeamProjectCollection tfsCollection;

        private VersionControlServer versionControlServer;
        private IBuildServer buildServer;
        private Workspace workspace;
        private List<WorkingFolder> workingFolders;
        private List<TeamProject> tfsProjects;
        private BranchObject[] allBranches;
        private Dictionary<TeamProject, WorkingFolder> folderByProject;

        private string userName;

        public MainWindow()
        {
            InitializeComponent();
        }

        private TeamProject SelectedProject
        {
            get { return ProjectsCombo.SelectedItem as TeamProject; }
        }

        private BranchObject SelectedBranch
        {
            get { return BranchesCombo.SelectedItem as BranchObject; }
        }

        private string CurrentServerFolder
        {
            get
            {
                if (SelectedBranch != null)
                {
                    return SelectedBranch.Properties.RootItem.Item;
                }
                else if (SelectedProject != null)
                {
                    return folderByProject[SelectedProject].ServerItem;
                }
                else
                {
                    return null;
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            tfsCollection = TfsTeamProjectCollectionFactory.GetTeamProjectCollection(RegisteredTfsConnections.GetProjectCollections().Single());
            versionControlServer = tfsCollection.GetService<VersionControlServer>();

            userName = tfsCollection.AuthorizedIdentity.UniqueName;

            workspace = versionControlServer.QueryWorkspaces(null, null, System.Environment.MachineName).Single();
            workingFolders = workspace.Folders.Where(f => !f.IsCloaked).ToList();

            tfsProjects = new List<TeamProject>();
            folderByProject = new Dictionary<TeamProject, WorkingFolder>();
            foreach (var folder in workingFolders)
            {
                var project = workspace.GetTeamProjectForLocalPath(folder.LocalItem);
                if (!folderByProject.ContainsKey(project))
                {
                    tfsProjects.Add(project);
                    folderByProject.Add(project, folder);
                }
            }

            ProjectsCombo.ItemsSource = tfsProjects;
            allBranches = versionControlServer.QueryRootBranchObjects(RecursionType.Full);
        }

        private void initBuild()
        {
            buildServer = tfsCollection.GetService<IBuildServer>();
            var allBuilds = buildServer.QueryBuildDefinitions("*");


            MessageBox.Show(this, string.Join("\n", allBuilds.Select(b => b.TeamProject + "\\" + b.Name).OrderBy(e => e)));
        }

        private void ProjectsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SelectedProject != null)
            {
                BranchesCombo.ItemsSource = allBranches.Where(b => b.Properties.RootItem.Item.Contains(folderByProject[SelectedProject].ServerItem)).ToList();
            }
        }

        private void CommentSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                SearchComments(CommentSearchTextBox.Text, userName);
            }
        }

        private void CommentSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchComments(CommentSearchTextBox.Text, userName);
        }

        private void ViewBranchHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentServerFolder != null)
            {
                var history = GetFullHistoryOfServerFolder(CurrentServerFolder, userName);

                FillGrid(history);
            }
        }

        private void SearchFileHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new Microsoft.Win32.OpenFileDialog
            {
                FileName = "",
                DefaultExt = "",
                Filter = "All Files (*.*)|*.*",
                Multiselect = false
            };

            if (fileDialog.ShowDialog(this) ?? false)
            {
                var serverFilePath = GetServerPathOfFile(fileDialog.FileName);
                var history = GetFileHistory(serverFilePath, userName);

                var fileChanges = (from changeset in history
                                   from change in changeset.Changes
                                   where new[] { ChangeType.Edit, ChangeType.Merge }.Contains(change.ChangeType)
                                         && change.Item.ItemType == ItemType.File
                                         && change.Item.ServerItem == serverFilePath
                                   select change).ToList();

                FillGrid(history);
            }
        }

        private void SearchComments(string searchTerm, string userName)
        {
            if (CurrentServerFolder != null)
            {
                var history = GetFullHistoryOfServerFolder(CurrentServerFolder, userName);

                var filteredHistory = from changeSet in history
                                      from searchWord in searchTerm.Split(new[] { ' ', '\r', '\n', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                      where changeSet.Comment.ToLowerInvariant().Contains(searchWord.ToLowerInvariant())
                                      select changeSet;

                FillGrid(string.IsNullOrWhiteSpace(searchTerm) ? history : filteredHistory);
            }
        }

        private List<Changeset> GetFullHistoryOfServerFolder(string serverFolder, string userName)
        {
            return versionControlServer.QueryHistory(serverFolder,
                                                     VersionSpec.Latest,
                                                     0,
                                                     RecursionType.Full,
                                                     userName,
                                                     new DateVersionSpec(new DateTime(2011, 1, 1)),
                                                     VersionSpec.Latest,
                                                     1000,
                                                     true,
                                                     false).Cast<Changeset>().ToList();
        }

        private void FillGrid(IEnumerable<Changeset> changesets)
        {
            var formatedResults = from changeset in changesets
                                  select new
                                  {
                                      Id = changeset.ChangesetId,
                                      Owner = changeset.Owner,
                                      Date = changeset.CreationDate,
                                      Comment = changeset.Comment,
                                      Files = string.Join("\r\n", changeset.Changes.Select(c => c.ChangeType.ToString() + " : " + GetShortFilePath(c.Item)))
                                  };

            ResultGrid.ItemsSource = formatedResults.Distinct().ToList();
        }

        private IEnumerable<DiffSegment> GetDiffs(Item file1, Item file2)
        {
            var tempFile1 = GetTfsFile(file1);
            var tempFile2 = GetTfsFile(file2);

            var result = Difference.DiffFiles(tempFile1, FileType.Detect(tempFile1, null), tempFile2, FileType.Detect(tempFile2, null), new DiffOptions() { OutputType = DiffOutputType.Unified });

            while (result != null)
            {
                yield return result;
                result = result.Next;
            }
        }

        private void WriteDiffsToFile(Item file, VersionSpec sourceVersion, VersionSpec targetVersion, FileInfo outputFile)
        {
            var sourceItem = Difference.CreateTargetDiffItem(versionControlServer, file.ServerItem, VersionSpec.Latest, 0, sourceVersion);
            var targetItem = Difference.CreateTargetDiffItem(versionControlServer, file.ServerItem, VersionSpec.Latest, 0, targetVersion);

            using (var outputStream = outputFile.CreateText())
            {
                WriteDiffsToStream(file, sourceItem, targetItem, outputStream);
            }
        }
        private void WriteDiffsToStream(Item file, IDiffItem sourceItem, IDiffItem targetItem, StreamWriter outputStream)
        {
            var diffOptions = new DiffOptions
            {
                SourceEncoding = Encoding.GetEncoding(file.Encoding),
                TargetEncoding = Encoding.GetEncoding(file.Encoding),
                OutputType = DiffOutputType.Unified,
                StreamWriter = outputStream,
            };

            Difference.DiffFiles(this.versionControlServer, sourceItem, targetItem, diffOptions, "AllYourBaseAreBelongToUs", true);
        }


        private List<Changeset> GetFileHistory(string serverFilePath, string userName = null)
        {
            var history = versionControlServer.QueryHistory(serverFilePath,
                                                            VersionSpec.Latest,
                                                            0,
                                                            RecursionType.Full,
                                                            userName,
                                                            new DateVersionSpec(new DateTime(2011, 1, 1)),
                                                            VersionSpec.Latest,
                                                            1000,
                                                            true,
                                                            false).Cast<Changeset>().ToList();

            return history;
        }

        private string GetShortFilePath(Item fileItem)
        {
            var workingFolder = GetWorkingFolderOfServerFile(fileItem.ServerItem);
            return fileItem.ServerItem.Replace(workingFolder.ServerItem, "");
        }

        private string GetServerPathOfFile(string localFilePath)
        {
            var workingFolder = GetWorkingFolderOfLocalFile(localFilePath);
            return localFilePath.ToLowerInvariant().Replace(workingFolder.LocalItem.ToLowerInvariant(), workingFolder.ServerItem.ToLowerInvariant());
        }

        private WorkingFolder GetWorkingFolderOfServerFile(string serverFilePath)
        {
            return (from workingFolder in workingFolders
                    where serverFilePath.ToLowerInvariant().Contains(workingFolder.ServerItem.ToLowerInvariant())
                    select workingFolder).SingleOrDefault();
        }

        private WorkingFolder GetWorkingFolderOfLocalFile(string localFilePath)
        {
            return (from workingFolder in workingFolders
                    where localFilePath.ToLowerInvariant().Contains(workingFolder.LocalItem.ToLowerInvariant())
                    select workingFolder).SingleOrDefault();
        }

        private static Dictionary<int, string> _downloadedItems = new Dictionary<int, string>();

        private static string GetTfsFile(Item file)
        {
            string result;
            if (_downloadedItems.TryGetValue(file.ItemId, out result))
            {
                return result;
            }
            else
            {
                result = System.IO.Path.GetTempFileName();
                file.DownloadFile(result);
                return result;
            }
        }

        /* TFS API Link for diffing files
         * http://blogs.msdn.com/b/buckh/archive/2006/04/06/project-diff.aspx
         * http://blogs.msdn.com/b/jmanning/archive/2006/03/27/562195.aspx
         * http://stackoverflow.com/questions/9474291/diffing-using-the-tfs-api
         * http://blogs.msdn.com/b/mohamedg/archive/2009/03/08/how-to-diff-files-using-tfs-apis.aspx
         * http://social.msdn.microsoft.com/Forums/br/tfsversioncontrol/thread/7e473d46-888d-468b-b618-bb80655848f0
         */
    }
}
