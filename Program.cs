//
// (C) Copyright 2014 by Autodesk, Inc.
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

[assembly: Autodesk.AutoCAD.Runtime.ExtensionApplication(null)]
[assembly: Autodesk.AutoCAD.Runtime.CommandClass(typeof(P360Share.Program))]

namespace P360Share
{
    public class Program
    {


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
        /// CLI version of _PLANTPROJECTCOLLABORATION
        /// Takes a ACC Hub and Project Ids and uploads the project to the Collaboration for Plant3D
        /// </summary>
        /// <param name="hubName"></param>
        /// <param name="projectId"></param>
        /// <param name="cts"></param>

        public static void UploadProject(string hubName, string projectId, CancellationToken cts)
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
                        var hub = hubs.FirstOrDefault(x => x.Name == hubName);
                        if (hub != null)
                        {
                            var projects = login.SelectA360ProjectsFromHub(hub, cts);

                            if (projects != null)
                            {
                                docProject = projects.FirstOrDefault(x => x.A360ProjectId == projectId);

                            }
                        }
                    }
                }
                , cts).Wait(cts);
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
                prj.EnableDocumentManagement(docsrv, string.Empty, string.Empty, docProject, assoc, cts);
                prj.Close();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.Assert(false, ex.Message);
            }
        }

       

        public static void PrintMsg(string msg)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc is null) return;

            doc.Editor.WriteMessage(msg);
        }

        /// <summary>
        /// Command to share a Plant3D project to Collaboration for Plant3D
        /// A CLI version of _PLANTPROJECTCOLLABORATION
        /// </summary>


        [CommandMethod("CLIPLANT360SHARE", CommandFlags.Modal)]

        public static void CLIPLANT360SHARE()
        {
            //ACC 360 Hub and Project Ids
            const string hubName = "Developer Advocacy Support"; //HubId - "b.489c5e7a-c6c0-4212-81f3-3529a621210b"
            const string projectId = "b.1549f155-5acf-4359-a496-f734a2ab05dd"; //ProjectId - "PLNT3D-DEV-ADVOCACY"           

            PrintMsg("Uploading project to Collaboration for Plant3D ACC...");
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(5)); // Cancel after 5 mins
            try
            {
                UploadProject(hubName, projectId, cts.Token);
            }
            catch (OperationCanceledException)
            {
                PrintMsg("The operation was canceled.");
            }
        }

               
    }
}
