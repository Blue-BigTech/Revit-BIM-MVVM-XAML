using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Remoting.Lifetime;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace commonAreas
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    internal class tailorbirdUpdater : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument; //e) First attribute (commandData) is used to acces the Revit user interface
            Document doc = uidoc.Document; // accesing data to the current project
            UIApplication uiapp = uidoc.Application;

            #region 1) Check if there are new updates
            //bool updateExist = false;

            //i). get new version number from a server
            WebClient webClient = new WebClient();

            //method 1: use raw content from public github repo
            string gitHubVersion = webClient.DownloadString(@"https://raw.githubusercontent.com/MiguelG97/TailorbirdAddins/main/commonareasVersion.txt");

            //somehow it adds an empty character at the end of the string
            if (gitHubVersion == null)
            {
                TaskDialog.Show("Tailorbird Exception", "commonareasVersion.txt file does not exist in github");
                return Result.Failed;
            }
            gitHubVersion = gitHubVersion.Remove(gitHubVersion.Length - 1);

            Assembly assembly = Assembly.GetExecutingAssembly(); //use this one!

            if (assembly != null)
            {
                //ii). Get assembly Versions
                Version versionInRevit = assembly.GetName().Version; // this one works as a string

                //iii) confirm if there is a new version (assuming user will alwys have an older version, no reason for them to have a later version)
                if (versionInRevit.ToString() != gitHubVersion)
                {
                    //TaskDialog.Show("de", "There is new version, do you wish to save your changes, close the Revit and update the plugin now?");
                    if (System.Windows.Forms.MessageBox.Show("Current version: " + versionInRevit.ToString() + "\n" + "Latest version: " + gitHubVersion + "\n" +
                        "Looks like there is an update! Do you want to save your changes, close Revit and update the latest version?",
                        "Tailorbird Update", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        //updateExist = true;

                        //save Revit changes and close revit
                        string docPath = doc.PathName;

                        if (docPath != string.Empty)
                        {
                            doc.Save();
                            //doc.Close(); //Revit encountered a The active document may not be closed from the API

                            #region 2) Download new dll file and replace it in the app bundle folder

                            //2.1) Get dll path to app bundle
                            string bundlePath = @"C:\ProgramData\Autodesk\ApplicationPlugins\tailorbirdCommonAreas.bundle\Contents\Sources";
                            string dllPathInBundle = bundlePath + @"\commonAreasApp.dll";

                            string tempPathToMove = Path.Combine(Path.GetTempPath(), "commonAreasApp.dll");
                            //bool re = false;

                            if (File.Exists(dllPathInBundle))
                            {
                                FileSecurity fileSec = File.GetAccessControl(dllPathInBundle);
                                fileSec.SetOwner(WindowsIdentity.GetCurrent().Owner);
                                //fileSec.ModifyAccessRule(AccessControlModification.Set, new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                                //FileSystemRights.FullControl, InheritanceFlags.None,
                                //PropagationFlags.None, AccessControlType.Allow), out re);

                                fileSec.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().Name, FileSystemRights.FullControl, InheritanceFlags.None, PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
                                fileSec.SetAccessRuleProtection(true, false);
                                File.SetAccessControl(dllPathInBundle, fileSec);

                                //TaskDialog.Show("de", pathToMove);
                                File.Move(dllPathInBundle, tempPathToMove);

                                //File.Delete(tempPathToMove);// I cant still delete this file that goes to temp files
                            }
                            else { 
                                //no thing, just download file from github and place to this path. Do this in the next lines
                            }

                            //delete previous dll
                            //File.SetAttributes(pathToMove, FileAttributes.Normal);
                            //File.Delete(dllPathInBundle);

                            //2.2) Get dll file path from github
                            string githubRawFile = @"https://raw.githubusercontent.com/MiguelG97/TailorbirdAddins/main/commonAreasSources/updaterDemo63.dll";
                            webClient.DownloadFile(githubRawFile, dllPathInBundle);//perhaps an exception for this one?

                            #endregion



                            RevitCommandId closeDoc = RevitCommandId.LookupPostableCommandId(PostableCommand.Close);
                            uiapp.PostCommand(closeDoc);

                            //You can't kill revit here, or the add-in will die!
                            foreach (Process process in Process.GetProcessesByName("Revit"))
                            {
                                process.Kill();
                            }

                        }
                        else
                        {

                            System.Windows.Forms.SaveFileDialog dialogSave = new System.Windows.Forms.SaveFileDialog();
                            dialogSave.Filter = "Project|.rvt";
                            dialogSave.Title = "Save Revit Project";
                            dialogSave.OverwritePrompt = false;
                            DialogResult result = dialogSave.ShowDialog();
                            if (result == System.Windows.Forms.DialogResult.Cancel)
                            {
                                return Result.Cancelled;
                            }

                            string projectRevitPath = dialogSave.FileName;
                            doc.SaveAs(projectRevitPath);


                            #region 2) Download new dll file and replace it in the app bundle folder

                            //2.1) Get dll path to app bundle
                            string bundlePath = @"C:\ProgramData\Autodesk\ApplicationPlugins\tailorbirdCommonAreas.bundle\Contents\Sources";
                            string dllPathInBundle = bundlePath + @"\commonAreasApp.dll";


                            string pathToMove = @"C:\ProgramData\Autodesk\ApplicationPlugins\tailorbirdCommonAreas.bundle\Contents\tailorbird.dll";
                            if (File.Exists(pathToMove))
                            {
                                File.Delete(pathToMove);//SOMEHOW PREVIOUS DLL FILES I CAN DELETE THEM AS THEY ARE NOT USED BY REVIT
                            }
                            File.Move(dllPathInBundle, pathToMove);

                            //delete previous dll
                            //File.Delete(pathToMove);

                            //2.2) Get dll file path from github
                            string githubRawFile = @"https://raw.githubusercontent.com/MiguelG97/TailorbirdAddins/main/commonAreasSources/updaterDemo63.dll";
                            webClient.DownloadFile(githubRawFile, dllPathInBundle);

                            #endregion




                            RevitCommandId closeDoc = RevitCommandId.LookupPostableCommandId(PostableCommand.Close);
                            uiapp.PostCommand(closeDoc);

                            //You can't kill revit here, or the add-in will die!
                            foreach (Process process in Process.GetProcessesByName("Revit"))
                            {
                                process.Kill();
                            }
                        }



                    }
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show("You have the latest version" + "\n" + "Current version: " + versionInRevit.ToString() + "\n" + "Latest version: " + gitHubVersion, "Tailorbird Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }


            }
            else
            {
                TaskDialog.Show("Tailorbird Exception", gitHubVersion + " ; " + "null value");
                return Result.Failed;
            }
            #endregion

            return Result.Succeeded;
        }
    }
}

