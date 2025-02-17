//
// (C) Copyright 2024 by Autodesk, Inc.
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
//
// Use, duplication, or disclosure by the U.S. Government is subject to
// restrictions set forth in FAR 52.227-19 (Commercial Computer
// Software - Restricted Rights) and DFAR 252.227-7013(c)(1)(ii)
// (Rights in Technical Data and Computer Software), as applicable.
//


using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
// AutoCAD
using Autodesk.AutoCAD.Runtime;
// Plant
using Autodesk.ProcessPower.DataObjects;        // PnPDataObjects.dll
using Autodesk.ProcessPower.P3dProjectParts;    // PnP3dProjectPartsMgd.dll
using Autodesk.ProcessPower.PlantInstance;      // PnPProjectManagerMgd.dll
using Autodesk.ProcessPower.ProjectManager;     // PnPProjectManagerMgd.dll
using Autodesk.ProcessPower.ProjectManagerUI;   // PnPProjectManagerUI.dll


using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Exception = System.Exception;

[assembly: Autodesk.AutoCAD.Runtime.ExtensionApplication(null)]
[assembly: Autodesk.AutoCAD.Runtime.CommandClass(typeof(P360Share.Program))]

namespace P360Share
{
    public class Program
    {

        [DllImport("accore.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "?sendBeat@AcApHeart@@YAXXZ")]
        private static extern void AcApHeartSendBeat();
        private static readonly string[] DcfSuffixes =
            {
                "ProcessPower.dcf", "Piping.dcf", "Ortho.dcf", "Iso.dcf", "Misc.dcf"
            };

        public static bool ConvertSQLServerProjectToSQLiteProject(
        string projPath, string userName, string password, CancellationToken cancellationToken = default)
        {
            try
            {
                foreach (string suffix in DcfSuffixes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string dcfFilePath = Path.Combine(projPath, suffix);
                    string tmpFilePath = $"{dcfFilePath}.new";

                    if (File.Exists(dcfFilePath))
                    {
                        ConvertDatabaseToSQLite(dcfFilePath, tmpFilePath, userName, password);

                        if (File.Exists(tmpFilePath))
                        {
                            File.Delete(dcfFilePath);
                            File.Move(tmpFilePath, dcfFilePath);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Operation canceled by the user.");
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error: {ex.Message}\nDetails: {ex}");
                return false;
            }

            return true;
        }

        private static void ConvertDatabaseToSQLite(string sourceFile, string targetFile, string userName, string password)
        {
            using (var database = PnPDatabase.Open(sourceFile, userName, password))
            {
                var targetLink = new PnPDatabaseLink
                {
                    DatabaseEngine = PnPDatabase.SQLiteEngineClass
                };
                targetLink.Add("Data Source", targetFile);

                using (var resultDb = PnPDatabase.CreateCopy(targetLink, database))
                {
                    resultDb.Close();
                }
            }
        }

        /// <summary>
        /// Deletes the Plant Collaboration Cache file
        /// </summary>

        private static void ClearCollobarationCache()
        {
            string appDataRoamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string plantCollaborationCachePath = Path.Combine(appDataRoamingPath, "Autodesk", "AutoCAD Plant 3D", "CollaborationCache", "project.client.cache");
            if (File.Exists(plantCollaborationCachePath))
            {
                PrintMsg("Deleting Plant Collaboration Cache file");
                File.Delete(plantCollaborationCachePath);
            }
        }



        /// <summary>
        /// CLI version of _PLANTPROJECTCOLLABORATION
        /// Takes a ACC Hub and Project Ids and uploads the project to the Collaboration for Plant3D
        /// </summary>
        /// <param name="hubName"></param>
        /// <param name="projectId"></param>
        /// <param name="cts"></param>

        [SupportedOSPlatform("windows")]
        public static void UploadProject(string hubName, string projectId, string folderId,CancellationToken cts)
        {
            try
            {
                //ToDo: Add support for Vault and SQL Server projects
                // Would be more complicated for Vault and SQL Server projects
                // Get the current project
                // Loading Project.xml have known issues with Document synchronization.
                PlantProject plantPrj = PlantApplication.CurrentProject;
               

                string projPath = plantPrj.ProjectFolderPath;

                // Server login
                //
                DocLogIn login = Commands.ServiceLogIn(Commands.CloudServiceName);
                if (login == null)
                {
                    // Sign in
                    //
                    Commands.PlantCloudLogin();
                    login = Commands.ServiceLogIn(Commands.CloudServiceName);
                    if (login == null)
                    {
                        return;
                    }
                }

                // Select Docs Hub and Project knowing their names or Ids
                //                
                DocA360Project docProject = null;              

                Task.Run(() =>
                {
                    var hubs = login.SelectA360Hubs(null, cts);
                    if (hubs != null)
                    {
                        var hub = hubs.FirstOrDefault(x => x.A360HubId == hubName);
                        if (hub != null)
                        {
                            var projects = login.SelectA360ProjectsFromHub(hub, cts);

                            if (projects != null)
                            {
                                docProject = projects.FirstOrDefault(x => x.A360ProjectId == projectId);
                                var folders = login.SelectA360ProjectSubFolders(docProject, cts);
                                if (folders != null)
                                {
                                    var folder = folders.FirstOrDefault(x => x.RootFolderUrn == folderId);
                                    if (folder != null)
                                    {
                                        docProject = folder;
                                        
                                    }
                                }


                            }
                        }
                    }
                }, cts).Wait(cts);
                if (docProject == null)
                {
                    return;
                }

                // Copy project to CollaborationCache
                //
                string destPath = Path.Combine(Commands.P360WorkingFolder.Trim(), plantPrj.Name);
                System.IO.DirectoryInfo sourceInfo = new DirectoryInfo(projPath);
                System.IO.DirectoryInfo destInfo = new DirectoryInfo(destPath);
                Utils.BackupProjectFiles(sourceInfo, destInfo);

                // If SQl Server project, convert to SQLite
                //
                var pid = plantPrj.ProjectParts["PnId"];
                if(pid != null)
                {
                    PnPDatabase db = pid.DataLinksManager.GetPnPDatabase();
                    if (db.DBEngine.GetType().ToString() != PnPDatabase.SQLiteEngineClass)
                    {
                        ConvertSQLServerProjectToSQLiteProject(destPath, plantPrj.Username, plantPrj.Password, cts);
                    }
                }               
                plantPrj.Close();

                // Load new project
                //
                PlantProject prj = PlantProject.LoadProject(Path.Combine(destPath, "Project.xml"), true, null, null);
               

                // Create potentially missing folders
                //
                string dir = Path.Combine(destPath, "Project Recycle Bin");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var iso = prj.ProjectParts["ISO"] as IsoProject;
                foreach (string folder in Directory.GetDirectories(iso.IsometricFolderPath))
                {
                    if (File.Exists(Path.Combine(folder, "IsoConfig.xml")))
                    {
                        dir = Path.Combine(folder, "PCFs");
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        dir = Path.Combine(folder, "ProdIsos", "Drawings");
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        dir = Path.Combine(folder, "QuickIsos", "Drawings");
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                    }
                }

                // Collect xrefs
                //
                var assoc = new Dictionary<PnPProjectFile, List<ProjectFileAssociation>>();
                foreach (Project p in prj.ProjectParts)
                {
                    List<PnPProjectItem> items = p.GetProjectItemsByType(p is MiscProject ? PnPProjectItem.MISCTYPE : PnPProjectItem.DWGTYPE);
                    foreach (PnPProjectItem item in items)
                    {
                        if (item is PnPProjectFile file)
                        {
                            var assocFiles = p.SelectRelatedProjectFiles(file).Select(t =>
                            {
                                var ret = new ProjectFileAssociation(file, t.Item1, t.Item2)
                                {
                                    IsXrefAttach = t.Item3,
                                    IsXrefNested = t.Item4
                                };
                                return ret;
                            }).ToList();

                            if (assocFiles.Count > 0)
                            {
                                assoc[file] = assocFiles;
                            }
                        }
                    }
                }

                // Share
                //
                PnPDocumentServerFactory fact = PnPDocumentServerFactoryRegistry.GetFactory(Commands.CloudServiceName);
                DocumentServer docsrv = fact.CreateInstance(Guid.NewGuid().ToString());               
                docsrv.SignIn(login, null);    
                using(var progress = new frmProgressDialog(docsrv, "uploading project","uploading",100))
                {
                    progress.Show();
                    prj.EnableDocumentManagement(docsrv, string.Empty, string.Empty, docProject, assoc, cts);
                  
                }
                
                prj.Close();
               
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.Assert(false, ex.Message);
            }
            PrintMsg("Project uploaded to Collaboration for Plant3D ACC");
        }

        public static async Task ShowProgressAsync(CancellationToken token)
        {
            int dots = 0;
            while (!token.IsCancellationRequested)
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc is null) return;
                doc.Editor.WriteMessage($"\rProcessing{new string('.', dots % 4)}   "); // Animated dots
                dots++;
                await Task.Delay(1000, token); // Update every 1s
            }
           PrintMsg("\rProgress stopped.                      ");
        }


        public static void PrintMsg(string msg)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc is null) return;

            doc.Editor.WriteMessage($"\n{msg}");
        }

        public static void PrintExceptionDetails(System.Exception ex)
        {
            if (ex is AggregateException aggEx)
            {
                aggEx = aggEx.Flatten(); // Unwrap nested AggregateException
                foreach (var innerEx in aggEx.InnerExceptions)
                {
                    PrintExceptionDetails(innerEx);
                }
            }
            else
            {
                while (ex != null)
                {
                    PrintMsg($"Exception: {ex.Message}");
                    ex = ex.InnerException;
                }
            }
        }

        /// <summary>
        /// Uploads a Plant3D project to Collaboration for Plant3D asynchronously
        /// Emits a CLI progress indicator
        /// </summary>
        /// <param name="hubName"></param>
        /// <param name="projectId"></param>
        /// <param name="folderId"></param>
        /// <returns></returns>

        public static async Task UploadProjectAsync(string hubName, string projectId, string folderId)
        {
            using CancellationTokenSource cts = new();

            try
            {
                // TODO: Add support for Vault and SQL Server projects
                PlantProject plantPrj = PlantApplication.CurrentProject;
                string projPath = plantPrj.ProjectFolderPath;

                // Server login
                DocLogIn login = Commands.ServiceLogIn(Commands.CloudServiceName);
                if (login == null)
                {
                    // Attempt Sign-in
                    Commands.PlantCloudLogin();
                    login = Commands.ServiceLogIn(Commands.CloudServiceName);
                    if (login == null) return;
                }                

                // Select Docs Hub and Project
                DocA360Project docProject = await Task.Run(() =>
                {
                    var hubs = login.SelectA360Hubs(null, cts.Token);
                    var hub = hubs?.FirstOrDefault(x => x.A360HubId == hubName);
                    var projects = hub != null ? login.SelectA360ProjectsFromHub(hub, cts.Token) : null;

                    var proj = projects?.FirstOrDefault(x => x.A360ProjectId == projectId);
                    if (proj != null && proj.RootFolderUrn == folderId) return null;

                    return login.SelectA360ProjectSubFolders(proj, cts.Token)?.FirstOrDefault(x => x.RootFolderUrn == folderId) ?? proj;
                }, cts.Token);

                if (docProject == null) return;

                // Copy project to CollaborationCache
                string destPath = Path.Combine(Commands.P360WorkingFolder.Trim(), plantPrj.Name);
                Utils.BackupProjectFiles(new DirectoryInfo(projPath), new DirectoryInfo(destPath));

                // Convert SQL Server project to SQLite if necessary
                var pid = plantPrj.ProjectParts["PnId"];
                if (pid?.DataLinksManager.GetPnPDatabase().DBEngine.ToString() != PnPDatabase.SQLiteEngineClass)
                {
                    ConvertSQLServerProjectToSQLiteProject(destPath, plantPrj.Username, plantPrj.Password, cts.Token);
                }

                plantPrj.Close();

                // Load new project
                PlantProject prj = PlantProject.LoadProject(Path.Combine(destPath, "Project.xml"), true, null, null);

                // Ensure project structure and collect XRefs
                EnsureProjectFoldersExist(prj);
                var assoc = CollectXrefs(prj);

                // Share project
                PnPDocumentServerFactory factory = PnPDocumentServerFactoryRegistry.GetFactory(Commands.CloudServiceName);
                DocumentServer docsrv = factory.CreateInstance(Guid.NewGuid().ToString());
                docsrv.SignIn(login, null);

                // Start progress indicator
                Task progressTask = ShowProgressAsync(cts.Token);

                try
                {
                    PrintMsg("Starting EnableDocumentManagement...");

                    await Task.Run(() =>
                    {
                        prj.EnableDocumentManagement(docsrv, string.Empty, string.Empty, docProject, assoc, cts.Token);
                        cts.Token.ThrowIfCancellationRequested(); // Ensures proper cancellation handling
                    }, cts.Token);

                    PrintMsg("EnableDocumentManagement completed successfully.");
                }
                catch (OperationCanceledException ex)
                {
                    PrintMsg($"Upload canceled: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    PrintExceptionDetails(ex);
                    throw;
                }
                finally
                {
                    cts.Cancel(); // Stop progress
                    await progressTask;
                    prj.Close();
                }
            }
            catch (OperationCanceledException)
            {
                PrintMsg("Upload operation was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                PrintExceptionDetails(ex);
                throw;
            }
        }

        /// <summary>
        /// Create auxiliary folders for a PlantProject
        /// </summary>
        /// <param name="prj"></param>

        private static void EnsureProjectFoldersExist(PlantProject prj)
        {
            string recycleBin = Path.Combine(prj.ProjectFolderPath, "Project Recycle Bin");
            if (!Directory.Exists(recycleBin)) Directory.CreateDirectory(recycleBin);

            var iso = prj.ProjectParts["ISO"] as IsoProject;
            foreach (string folder in Directory.GetDirectories(iso.IsometricFolderPath))
            {
                if (File.Exists(Path.Combine(folder, "IsoConfig.xml")))
                {
                    CreateIfMissing(folder, "PCFs");
                    CreateIfMissing(folder, "ProdIsos", "Drawings");
                    CreateIfMissing(folder, "QuickIsos", "Drawings");
                }
            }
        }

        /// <summary>
        /// Creates a directory if it does not exist
        /// </summary>
        /// <param name="paths"></param>
        private static void CreateIfMissing(params string[] paths)
        {
            string path = Path.Combine(paths);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        /// <summary>
        /// Collects Xrefs for a given PlantProject
        /// </summary>
        /// <param name="prj"></param>
        /// <returns></returns>
        private static Dictionary<PnPProjectFile, List<ProjectFileAssociation>> CollectXrefs(PlantProject prj)
        {
            var assoc = new Dictionary<PnPProjectFile, List<ProjectFileAssociation>>();

            foreach (Project p in prj.ProjectParts)
            {
                List<PnPProjectItem> items = p.GetProjectItemsByType(p is MiscProject ? PnPProjectItem.MISCTYPE : PnPProjectItem.DWGTYPE);
                foreach (PnPProjectItem item in items)
                {
                    if (item is PnPProjectFile file)
                    {
                        var assocFiles = p.SelectRelatedProjectFiles(file)
                                          .Select(t => new ProjectFileAssociation(file, t.Item1, t.Item2)
                                          {
                                              IsXrefAttach = t.Item3,
                                              IsXrefNested = t.Item4
                                          }).ToList();

                        if (assocFiles.Count > 0)
                        {
                            assoc[file] = assocFiles;
                        }
                    }
                }
            }
            return assoc;
        }

        /// <summary>
        ///  A CLI version of _PLANTPROJECTCOLLABORATION in async mode
        /// </summary>

        [CommandMethod("CLIPLANT360SHAREASYNC", CommandFlags.Modal)]
        public static async void CliPlant360ShareAsync()
        {

            ClearCollobarationCache();
            const string hubId = "b.489c5e7a-c6c0-4212-81f3-3529a621210b"; //HubId - "Developer Advocacy Support"
            const string projectId = "b.83bc11b3-2204-4ccc-9d44-4bdaf58d46f9"; //ProjectId - "PLNT3D-DEV-ADVOCACY"
            const string folderId = "urn:adsk.wipprod:fs.folder:co.7JtMkL6ARbGGHyv-q0yf7w"; //FolderId - "urn:adsk.wipprod:fs.folder:co.7JtMkL6ARbGGHyv-q0yf7w"

            PrintMsg("Uploading project to Collaboration for Plant3D ACC...");

            try
            {
                await UploadProjectAsync(hubId, projectId, folderId);
                PrintMsg("Project uploaded successfully to Collaboration for Plant3D ACC.");
            }
            catch (OperationCanceledException)
            {
                PrintMsg("The operation was canceled by the user.");
            }
            catch (Exception ex)
            {
                PrintExceptionDetails(ex);
                PrintMsg("An error occurred during upload.");
                
            }           
        }



        /// <summary>
        /// Command to share a Plant3D project to Collaboration for Plant3D
        /// A CLI version of _PLANTPROJECTCOLLABORATION
        /// </summary>

        [SupportedOSPlatform("windows")]
        [CommandMethod("CLIPLANT360SHARE", CommandFlags.Modal)]

        public static void CLIPLANT360SHARE()
        {

            ClearCollobarationCache();

            //ACC 360 Hub and Project Ids
            //const string hubName = "Developer Advocacy Support"; //HubId - "b.489c5e7a-c6c0-4212-81f3-3529a621210b"

            //ProjectId - "PLNT3D-DEV-ADVOCACY"           
            const string hubId = "b.46ab3082-1131-4843-b5d3-91ee4f586686";
            const string projectId = "b.6042b6ef-5ec3-4058-a632-ac37d13fb367";
            const string folderId = "urn:adsk.wipemea:fs.folder:co.umYU34VJTx60ipzJO5Wzhg";

            PrintMsg("Uploading project to Collaboration for Plant3D ACC...");
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(59));             
            try
            {
                UploadProject(hubId, projectId, folderId,cts.Token);
            }
            catch (OperationCanceledException)
            {
                PrintMsg("The operation was canceled.");
            }
        }
        
    }
}
