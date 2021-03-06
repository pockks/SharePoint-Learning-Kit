using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.IO;
using System.Web;
using Microsoft.SharePoint;
using Microsoft.SharePoint.WebControls;
using Microsoft.LearningComponents.SharePoint;
using Microsoft.LearningComponents.Storage;
using Resources.Properties;

namespace Microsoft.SharePointLearningKit
{
    /// <summary>
    /// Class for creating the folders and subfolders under the DropBox document library in both modes Create and Edit.
    /// </summary>
    public class DropBoxManager
    {
        AssignmentProperties assignmentProperties;
        DropBoxSettings settings;
        ISlkStore store;
        SlkCulture culture;

#region constructors
        /// <summary>Initializes a new instance of <see cref="DropBoxManager"/>.</summary>
        public DropBoxManager(AssignmentProperties assignmentProperties)
        {
            this.assignmentProperties = assignmentProperties;
            store = assignmentProperties.Store;
            this.settings = assignmentProperties.Store.Settings.DropBoxSettings;
            culture = new SlkCulture();
        }
#endregion constructors

#region properties
        SPUser CurrentUser
        {
            get { return SPContext.Current.Web.CurrentUser ;}
        }
#endregion properties

#region public methods
        /// <summary>Sets the correct permissions when the item is collected.</summary>
        /// <param name="user">The user to update the folder for.</param>
        public void ApplyCollectAssignmentPermissions(SPUser user)
        {
            // If the assignment is auto return, the learner will still be able to view the drop box assignment files
            // otherwise, learner permissions will be removed from the drop box library & learner's subfolder in the Drop Box document library
            SPRoleType learnerPermissions = SPRoleType.Reader;
            if (assignmentProperties.AutoReturn == false)
            {
                learnerPermissions = SPRoleType.None;
            }

            ApplyAssignmentPermission(user, learnerPermissions, SPRoleType.Contributor, true);
        }

        /// <summary>Sets the correct permissions when the item is returned to the learner.</summary>
        /// <param name="user">The user to update the folder for.</param>
        public void ApplyReturnAssignmentPermission(SPUser user)
        {
            ApplyAssignmentPermission(user, SPRoleType.Reader, SPRoleType.Reader, false);
        }

        /// <summary>Sets the correct permissions when the item is reactivated.</summary>
        /// <param name="user">The user to update the folder for.</param>
        public void ApplyReactivateAssignmentPermission(SPUser user)
        {
            ApplyAssignmentPermission(user, SPRoleType.Contributor, SPRoleType.Reader, true);
        }

        /// <summary>Copies the original file to the student's drop box.</summary>
        /// <returns>The url of the file.</returns>
        public AssignmentFile CopyFileToDropBox()
        {
            AssignmentFile assignmentFile = null;

            SPSecurity.RunWithElevatedPrivileges(delegate()
            {
                SharePointFileLocation fileLocation;
                if (!SharePointFileLocation.TryParse(assignmentProperties.Location, out fileLocation))
                {
                    throw new SafeToDisplayException(SlkFrameset.FRM_DocumentNotFound);
                }

                using (SPSite sourceSite = new SPSite(fileLocation.SiteId))
                {
                    using (SPWeb sourceWeb = sourceSite.OpenWeb(fileLocation.WebId))
                    {
                        SPFile file = sourceWeb.GetFile(fileLocation.FileId);
                        if (file.Exists == false)
                        {
                            string message = string.Format(CultureInfo.CurrentUICulture, culture.Resources.AssignmentFileDoesNotExist, fileLocation.FileId, assignmentProperties.Title);
                            store.LogError(message);
                            throw new SafeToDisplayException(message);
                        }

                        if (MustCopyFileToDropBox(file.Name))
                        {
                            try
                            {
                                assignmentFile = SaveFile(file);
                            }
                            catch (SPException)
                            {
                                // Retry in case a temporary error
                                try
                                {
                                    assignmentFile = SaveFile(file);
                                }
                                catch (SPException e)
                                {
                                    string message = string.Format(CultureInfo.CurrentUICulture, culture.Resources.DropBoxFailedToCopyFile, file.Name, assignmentProperties.Title, e.Message);
                                    store.LogError(message);
                                    string safeMessage = string.Format(CultureInfo.CurrentUICulture, culture.Resources.DropBoxFailedToCopyFile, file.Name, assignmentProperties.Title, string.Empty);
                                    throw new SafeToDisplayException(safeMessage);
                                }
                            }
                        }
                    }
                }
            });

            return assignmentFile;
        }

        /// <summary>Uploads files to the learner's drop box.</summary>
        /// <param name="files">The files to upload.</param>
        /// <param name="existingFilesToKeep">Existing files to keep.</param>
        public void UploadFiles(AssignmentUpload[] files, int[] existingFilesToKeep)
        {
            SPUser currentUser = CurrentUser;

            SPSecurity.RunWithElevatedPrivileges(delegate
            {
                using (SPSite spSite = new SPSite(assignmentProperties.SPSiteGuid))
                {
                    spSite.CatchAccessDeniedException = false;
                    using (SPWeb spWeb = spSite.OpenWeb(assignmentProperties.SPWebGuid))
                    {
                        DropBox dropBox = new DropBox(store, spWeb);
                        AssignmentFolder assignmentFolder = dropBox.GetOrCreateAssignmentFolder(assignmentProperties);
                        assignmentFolder.ApplyPermission(currentUser, SPRoleType.Reader);

                        AssignmentFolder learnerSubFolder = assignmentFolder.FindLearnerFolder(currentUser);

                        if (learnerSubFolder == null)
                        {
                            learnerSubFolder = assignmentFolder.CreateLearnerAssignmentFolder(currentUser);
                        }
                        else
                        {
                            learnerSubFolder.ResetIsLatestFiles(existingFilesToKeep, currentUser.ID);
                        }

                        CheckExtensions(spSite, files);

                        using (new AllowUnsafeUpdates(spWeb))
                        {
                            SlkUser currentSlkUser = new SlkUser(currentUser);
                            foreach (AssignmentUpload upload in files)
                            {
                                learnerSubFolder.SaveFile(upload.Name, upload.Stream, currentSlkUser);
                            }
                        }

                        ApplySubmittedPermissions(spWeb);
                    }
                }
            });
        }

        /// <summary>Applys permissions when the document is submitted.</summary>
        public void ApplySubmittedPermissions()
        {
            SPSecurity.RunWithElevatedPrivileges(delegate{
                using (SPSite spSite = new SPSite(assignmentProperties.SPSiteGuid))
                {
                    using (SPWeb spWeb = spSite.OpenWeb(assignmentProperties.SPWebGuid))
                    {
                        ApplySubmittedPermissions(spWeb);
                    }
                }
                    });
        }

        /// <summary>Returns all files for an assignment grouped by learner.</summary>
        public Dictionary<string, List<SPFile>> AllFiles()
        {
            using (SPSite site = new SPSite(assignmentProperties.SPSiteGuid, SPContext.Current.Site.Zone))
            {
                using (SPWeb web = site.OpenWeb(assignmentProperties.SPWebGuid))
                {
                    DropBox dropBox = new DropBox(store, web);
                    return dropBox.AllFiles(assignmentProperties.Id.GetKey());
                }
            }
            
        }

        /// <summary>Function to update the DropBox document library after the current assignment being edited</summary>
        /// <param name="oldAssignmentProperties">The old assignment properties</param>
        public void UpdateAssignment(AssignmentProperties oldAssignmentProperties)
        {
            SPSecurity.RunWithElevatedPrivileges(delegate
            {
                using (SPSite spSite = new SPSite(assignmentProperties.SPSiteGuid))
                {
                    using (SPWeb spWeb = spSite.OpenWeb(assignmentProperties.SPWebGuid))
                    {
                        using (new AllowUnsafeUpdates(spWeb))
                        {
                            DropBox dropBox = new DropBox(store, spWeb);

                            string oldAssignmentFolderName = DropBox.GenerateFolderName(oldAssignmentProperties);
                            string newAssignmentFolderName = DropBox.GenerateFolderName(assignmentProperties);

                            // If assignment title has been changed, create a new assignment folder and move old assignment folder contents to it
                            if (string.Compare(oldAssignmentFolderName,  newAssignmentFolderName, true, CultureInfo.InvariantCulture) != 0)
                            {
                                dropBox.ChangeFolderName(oldAssignmentFolderName, newAssignmentFolderName);
                            }

                            // Get new assignment folder, or the old one if the title has not been changed
                            // in both cases, the value of the current assignment folder name will be stored in newAssignmentFolderName
                            AssignmentFolder assignmentFolder = dropBox.GetAssignmentFolder(assignmentProperties);

                            if (assignmentFolder != null)
                            {
                                assignmentFolder.RemoveAllPermissions();

                                // Grant assignment instructors Read permission on the assignment folder
                                ApplyInstructorsReadAccessPermissions(assignmentFolder, spWeb, dropBox);

                                // Delete subfolders of the learners who have been removed from the assignment
                                DeleteRemovedLearnerFolders(assignmentFolder, oldAssignmentProperties);

                                foreach (SlkUser learner in assignmentProperties.Learners)
                                {
                                    // Grant assignment learners Read permission on the assignment folder and drop box
                                    assignmentFolder.ApplyPermission(learner.SPUser, SPRoleType.Reader);
                                    AssignmentFolder.ApplySharePointPermission(spWeb, dropBox.DropBoxList, learner.SPUser, SPRoleType.Reader);

                                    AssignmentFolder learnerSubFolder = assignmentFolder.FindLearnerFolder(learner.SPUser);
                                    LearnerAssignmentProperties result = assignmentProperties.ResultForLearner(learner);

                                    if (learnerSubFolder == null)
                                    {
                                        // Create a new subfolder for this learner
                                        learnerSubFolder = assignmentFolder.CreateLearnerAssignmentFolder(learner.SPUser);
                                    }

                                    if (result != null)
                                    {
                                        AssignUpdatePermissions(learnerSubFolder, learner.SPUser, result.Status);
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        void AssignUpdatePermissions(AssignmentFolder folder, SPUser learner, LearnerAssignmentState? status)
        {
            folder.RemoveAllPermissions();

            if (status != null)
            {
                switch (status.Value)
                {
                    case LearnerAssignmentState.NotStarted:
                        break;

                    case LearnerAssignmentState.Active:
                        ApplyInstructorsReadAccessPermissions(folder);
                        folder.ApplyPermission(learner, SPRoleType.Contributor);
                        break;

                    case LearnerAssignmentState.Completed:
                        ApplyInstructorsContributeAccessPermissions(folder);
                        break;

                    case LearnerAssignmentState.Final:
                        ApplyInstructorsReadAccessPermissions(folder);
                        folder.ApplyPermission(learner, SPRoleType.Reader);
                        break;
                }
            }
        }
        
        /// <summary>Returns the last submitted files for the given learner.</summary>
        public AssignmentFile[] LastSubmittedFiles(long learnerId, bool forceUnlock)
        {
            foreach (SlkUser learner in assignmentProperties.Learners)
            {
                if (learner.UserId.GetKey() == learnerId)
                {
                    return LastSubmittedFiles(learner.SPUser, forceUnlock);
                }
            }
            return new AssignmentFile[0];
        }

        /// <summary>Returns the last submitted files for the given learner.</summary>
        public AssignmentFile[] LastSubmittedFiles(SPUser user, bool forceUnlock)
        {
            if (user == null)
            {
                return null;
            }

            int currentUser = CurrentUser.ID;

            AssignmentFile[] toReturn = null;
            SPSecurity.RunWithElevatedPrivileges(delegate
            {
                using (SPSite spSite = new SPSite(assignmentProperties.SPSiteGuid))
                {
                    using (SPWeb spWeb = spSite.OpenWeb(assignmentProperties.SPWebGuid))
                    {
                        DropBox dropBox = new DropBox(store, spWeb);
                        toReturn = dropBox.LastSubmittedFiles(new SlkUser(user), assignmentProperties.Id.GetKey(), forceUnlock, currentUser);
                    }
                }
            });
            return toReturn;
        }


        /// <summary>Returns the last submitted files for the current user.</summary>
        public AssignmentFile[] LastSubmittedFiles(bool forceUnlock)
        {
            return LastSubmittedFiles(CurrentUser, forceUnlock);
        }

        /// <summary>Deletes the assignment folder.</summary>
        public void DeleteAssignmentFolder()
        {
            SPSecurity.RunWithElevatedPrivileges(delegate
            {
                using (SPSite spSite = new SPSite(assignmentProperties.SPSiteGuid))
                {
                    using (SPWeb spWeb = spSite.OpenWeb(assignmentProperties.SPWebGuid))
                    {
                        DropBox dropBox = new DropBox(store, spWeb);

                        //Get the folder if it exists 
                        AssignmentFolder assignmentFolder = dropBox.GetAssignmentFolder(assignmentProperties);

                        if (assignmentFolder != null)
                        {
                            using (new AllowUnsafeUpdates(spWeb))
                            {
                                assignmentFolder.Delete();
                            }
                        }
                    }
                }
            });
        }

        /// <summary>Creates an assignment folder.</summary>
        public void CreateAssignmentFolder()
        {
            Microsoft.SharePoint.Utilities.SPUtility.ValidateFormDigest();

            SPSecurity.RunWithElevatedPrivileges(delegate
            {
                using (SPSite spSite = new SPSite(assignmentProperties.SPSiteGuid))
                {
                    using (SPWeb spWeb = spSite.OpenWeb(assignmentProperties.SPWebGuid))
                    {
                        DropBox dropBox = new DropBox(store, spWeb);

                        //Get the folder if it exists 
                        if (dropBox.GetAssignmentFolder(assignmentProperties) != null)
                        {
                            throw new SafeToDisplayException(culture.Resources.AssFolderAlreadyExists);
                        }

                        AssignmentFolder assignmentFolder = dropBox.CreateAssignmentFolder(assignmentProperties);
                        ApplyInstructorsReadAccessPermissions(assignmentFolder, spWeb, dropBox);

                        //Create a Subfolder for each learner
                        foreach (SlkUser learner in assignmentProperties.Learners)
                        {
                            SPUser spLearner = learner.SPUser;
                            assignmentFolder.ApplyPermission(spLearner, SPRoleType.Reader);
                            AssignmentFolder.ApplySharePointPermission(spWeb, dropBox.DropBoxList, learner.SPUser, SPRoleType.Reader);
                            assignmentFolder.CreateLearnerAssignmentFolder(spLearner);
                        }
                    }
                }
            });
        }

        /// <summary>Creates an assignment folder.</summary>
        /// <returns>The created folder or null if it already exists.</returns>
        public AssignmentFolder CreateSelfAssignmentFolder()
        {
            AssignmentFolder assignmentFolder = null;

            SPSecurity.RunWithElevatedPrivileges(delegate
            {
                using (SPSite spSite = new SPSite(assignmentProperties.SPSiteGuid))
                {
                    using (SPWeb spWeb = spSite.OpenWeb(assignmentProperties.SPWebGuid))
                    {
                        DropBox dropBox = new DropBox(store, spWeb);

                        //Get the folder if it exists 
                        assignmentFolder = dropBox.GetAssignmentFolder(assignmentProperties);

                        //Create the assignment folder if it does not exist
                        if (assignmentFolder == null)
                        {
                            assignmentFolder = dropBox.CreateAssignmentFolder(assignmentProperties);
                            assignmentFolder.ApplyPermission(CurrentUser, SPRoleType.Reader);
                            assignmentFolder.CreateLearnerAssignmentFolder(CurrentUser);
                        }
                        else
                        {
                            assignmentFolder = null;
                        }
                    }
                }
            });
            return assignmentFolder;
        }

        /// <summary>Generate the drop box edit details.</summary>
        /// <param name="file">The file to edit.</param>
        /// <param name="web">The web the file is in.</param>
        /// <param name="mode">The open mode.</param>
        /// <param name="sourceUrl">The source page.</param>
        /// <returns></returns>
        public DropBoxEditDetails GenerateDropBoxEditDetails(AssignmentFile file, SPWeb web, DropBoxEditMode mode, string sourceUrl)
        {
            DropBoxEditDetails details = new DropBoxEditDetails();

            // Set default details
            details.Url = file.Url;
            details.OnClick = EditJavascript(file, web);

            try
            {
                bool isIpad = SlkUtilities.IsIpad();
                if (settings.UseOfficeWebApps)
                {
                    if (file.IsOfficeFile)
                    {
                        if (settings.OpenOfficeInIpadApp && isIpad)
                        {
                            details.Url = file.GenerateOfficeProtocolUrl(web, sourceUrl);
                            details.OnClick = null;
                        }
                        else if (mode == DropBoxEditMode.Edit)
                        {
                            // Document must be 2010 format to be edited in office web apps
                            if (file.IsOwaCompatible)
                            {
                                details.Url = file.GenerateOfficeAppsEditUrl(web, sourceUrl);
                                details.OnClick = null;
                            }
                        }
                        // Use Office Web Apps viewer for office files except for Excel which does not support it for pre 2010 worksheets
                        else if (file.Extension.ToUpperInvariant() != "XLS")
                        {
                            details.Url = file.GenerateOfficeAppsViewUrl(web, sourceUrl);
                            details.OnClick = null;
                        }
                    }
                }
                else if (settings.OpenOfficeInIpadApp && isIpad)
                {
                    details.Url = file.GenerateOfficeProtocolUrl(web, sourceUrl);
                    details.OnClick = null;
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // Not valid for office web apps
                details.Url = file.Url;
                details.OnClick = EditJavascript(file, web);
            }

            return details;
        }

        /// <summary>Unlocks a file.</summary>
        /// <param name="file">The file to unlock.</param>
        /// <param name="currentUser">The current user id.</param>
        public static void UnlockFile(SPFile file, int currentUser)
        {
            try
            {
                if (file.LockedByUser != null && file.LockedByUser.ID != currentUser)
                {
                    using (new AllowUnsafeUpdates(file.Web))
                    {
                        file.ReleaseLock(file.LockId);
                    }
                }
            }
            catch (SPException e)
            {
                SlkCulture culture = new SlkCulture();
                string message = string.Format(CultureInfo.CurrentUICulture, culture.Resources.FailUnlockFile, file.Item.Url);
                SlkStore.GetStore(file.Web).LogException(e);
                throw new SafeToDisplayException(message);
            }
        }
#endregion public methods

#region private methods
        private AssignmentFile SaveFile(SPFile file)
        {
            AssignmentFile assignmentFile = null;
            using (SPSite destinationSite = new SPSite(assignmentProperties.SPSiteGuid))
            {
                destinationSite.CatchAccessDeniedException = false;
                using (SPWeb destinationWeb = destinationSite.OpenWeb(assignmentProperties.SPWebGuid))
                {
                    using (new AllowUnsafeUpdates(destinationWeb))
                    {
                        // Temporarily turn off property promotion. Possible cause for exceptions saving docs. I don't actually know 
                        // if settings this will disable it as without an update it's not persisted and we don't want to persist the
                        // setting, just turn it off for this addition. It may even work asynchronously.
                        destinationWeb.ParserEnabled = false;
                        SPUser learner = CurrentUser;
                        DropBox dropBox = new DropBox(store, destinationWeb);
                        AssignmentFolder assignmentFolder = dropBox.GetAssignmentFolder(assignmentProperties);
                        AssignmentFolder learnerSubFolder = null;

                        if (assignmentFolder == null)
                        {
                            assignmentFolder = dropBox.CreateAssignmentFolder(assignmentProperties);
                        }

                        // ApplyAssignmentPermission creates the learner folder if required
                        ApplyAssignmentPermission(learner, SPRoleType.Contributor, SPRoleType.Reader, true);
                        learnerSubFolder = assignmentFolder.FindLearnerFolder(learner);

                        using (Stream stream = file.OpenBinaryStream())
                        {
                            assignmentFile = learnerSubFolder.SaveFile(file.Name, stream, new SlkUser(learner));
                        }
                    }
                }
            }

            return assignmentFile;
        }

        /// <summary>Sets the correct permissions when the item is submitted.</summary>
        /// <param name="web">The web the assignment is for.</param>
        void ApplySubmittedPermissions(SPWeb web)
        {
            DropBox dropBox = new DropBox(store, web);
            AssignmentFolder assignmentFolder = dropBox.GetAssignmentFolder(assignmentProperties);

            if (assignmentFolder == null)
            {
                assignmentFolder = dropBox.CreateAssignmentFolder(assignmentProperties);
            }
            else
            {
                AssignmentFolder learnerSubFolder = assignmentFolder.FindLearnerFolder(CurrentUser);
                ApplySubmittedPermissions(learnerSubFolder);
            }
        }

        void CheckExtensions(SPSite site, AssignmentUpload[] files)
        {
            Collection<string> blockedExtensions = site.WebApplication.BlockedFileExtensions;
            List<string> failures = new List<string>();

            foreach (AssignmentUpload upload in files)
            {
                if (CheckExtension(blockedExtensions, upload.Name) == false)
                {
                    failures.Add(upload.Name);
                }
            }

            if (failures.Count > 0)
            {
                string message = string.Format(culture.Culture, culture.Resources.FilesUploadPageFailureMessage, string.Join(", ", failures.ToArray()));
                throw new SafeToDisplayException(message);
            }
        }

        bool CheckExtension(Collection<string> blockedExtensions, string fileName)
        {
            string extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension))
            {
                return true;
            }
            else
            {
                extension = extension.ToLower(CultureInfo.InvariantCulture);
                if (extension[0] == '.')
                {
                    extension = extension.Substring(1);
                }

                if (blockedExtensions != null && blockedExtensions.Contains(extension))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        void DeleteRemovedLearnerFolders(AssignmentFolder assignmentFolder, AssignmentProperties oldAssignmentProperties)
        {
            foreach (SlkUser oldLearner in oldAssignmentProperties.Learners)
            {
                if (!assignmentProperties.Learners.Contains(oldLearner.UserId))
                {
                    // Get learner subfolder, and delete it if exists
                    AssignmentFolder learnerSubFolder = assignmentFolder.FindLearnerFolder(oldLearner.SPUser);
                    if (learnerSubFolder != null)
                    {
                        learnerSubFolder.Delete();
                    }
                }
            }
        }

        void ApplyInstructorsContributeAccessPermissions(AssignmentFolder folder)
        {
            foreach (SlkUser instructor in assignmentProperties.Instructors)
            {
                folder.ApplyPermission(instructor.SPUser, SPRoleType.Contributor);
            }
        }

        void ApplyInstructorsReadAccessPermissionsToDropBox(SPWeb web, DropBox dropBox)
        {
            foreach (SlkUser instructor in assignmentProperties.Instructors)
            {
                AssignmentFolder.ApplySharePointPermission(web, dropBox.DropBoxList, instructor.SPUser, SPRoleType.Reader);
            }
        }

        void ApplyInstructorsReadAccessPermissions(AssignmentFolder folder, SPWeb web, DropBox dropBox)
        {
            // In one instance had an issue that instructors couldn't see uploaded assignments in OWA if they
            // didn't have read access on the drop box.
            ApplyInstructorsReadAccessPermissionsToDropBox(web, dropBox);
            ApplyInstructorsReadAccessPermissions(folder);
        }

        void ApplyInstructorsReadAccessPermissions(AssignmentFolder folder)
        {
            foreach (SlkUser instructor in assignmentProperties.Instructors)
            {
                folder.ApplyPermission(instructor.SPUser, SPRoleType.Reader);
            }
        }

        /// <summary>Applies permissions to the learner folder and creates it if required.</summary>
        /// <param name="user">The learner to create the folder for.</param>
        /// <param name="learnerPermissions">The permissions to set.</param>
        /// <param name="instructorPermissions">The instructor permissions to set.</param>
        /// <param name="removeObserverPermissions">Whether to remove observer permissions or not.</param>
        void ApplyAssignmentPermission(SPUser user, SPRoleType learnerPermissions, SPRoleType instructorPermissions, bool removeObserverPermissions)
        {
            SPSecurity.RunWithElevatedPrivileges(delegate
            {
                using (SPSite spSite = new SPSite(assignmentProperties.SPSiteGuid))
                {
                    using (SPWeb spWeb = spSite.OpenWeb(assignmentProperties.SPWebGuid))
                    {
                        DropBox dropBox = new DropBox(store, spWeb);
                        AssignmentFolder assignmentFolder = dropBox.GetAssignmentFolder(assignmentProperties);

                        if (assignmentFolder != null)
                        {
                            // Get the learner sub folder
                            AssignmentFolder learnerSubFolder = assignmentFolder.FindLearnerFolder(user);

                            if (learnerSubFolder == null)
                            {
                                learnerSubFolder = assignmentFolder.CreateLearnerAssignmentFolder(user);
                            }
                            
                            // Apply learner permissions
                            learnerSubFolder.RemovePermissions(user);
                            if (learnerPermissions != SPRoleType.None)
                            {
                                learnerSubFolder.ApplyPermission(user, learnerPermissions);
                            }

                            // Apply instructor permissions
                            foreach (SlkUser instructor in assignmentProperties.Instructors)
                            {
                                learnerSubFolder.RemovePermissions(instructor.SPUser);
                                learnerSubFolder.ApplyPermission(instructor.SPUser, instructorPermissions);
                            }

                            if (removeObserverPermissions)
                            {
                                learnerSubFolder.RemoveObserverPermission();
                            }
                        }
                    }
                }
            });
        }

        void ApplySubmittedPermissions(AssignmentFolder learnerSubFolder)
        {
            // IF the assignment is auto return, the learner will still be able to view the drop box assignment files
            // otherwise, learner permissions will be removed from the learner's subfolder in the Drop Box document library
            if (assignmentProperties.AutoReturn == false)
            {
                learnerSubFolder.RemovePermissions(CurrentUser);
            }

            // Grant instructors contribute permission on learner subfolder
            foreach (SlkUser instructor in assignmentProperties.Instructors)
            {
                if (instructor.SPUser == null)
                {
                    throw new SafeToDisplayException(string.Format(culture.Culture, culture.Resources.DropBoxManagerUploadFilesNoInstructor, instructor.Name));
                }
                learnerSubFolder.RemovePermissions(instructor.SPUser);
                learnerSubFolder.ApplyPermission(instructor.SPUser, SPRoleType.Contributor);
            }

        }

#endregion private methods

#region public static methods
#endregion public static methods

#region static methods
        /// <summary>Generates the edit javascript script.</summary>
        /// <param name="file">The file to generate the js for.</param>
        /// <param name="web">The web the file is in.</param>
        /// <returns>The script.</returns>
        static string EditJavascript(AssignmentFile file, SPWeb web)
        {
            if (file.IsEditable)
            {
#if SP2007
                string script = "editDocumentWithProgID2('{0}', '', 'SharePoint.OpenDocuments','0','{1}','0');";
                return string.Format(CultureInfo.InvariantCulture, script, Microsoft.SharePoint.Utilities.SPHttpUtility.UrlPathEncode(file.Url, false), web.Url);
#else
                return CreateDispExFunctionCall(file, web);
#endif
            }
            else
            {
                return string.Empty;
            }
        }

        static string CreateDispExFunctionCall(AssignmentFile file, SPWeb web)
        {
            string text = Microsoft.SharePoint.Utilities.SPUtility.MapToControl(web, file.Name, string.Empty);
            // string openInBrowser = (file.Item.ParentList.DefaultItemOpen == DefaultItemOpen.Browser) ? "1" : "0";
            string openInBrowser = "0"; // Always 0 as want to open in client application
            // string forceCheckout = file.Item.ParentList.ForceCheckout ? "1" : "0";
            string forceCheckout = "0"; // Drop box doesn't support check in/out.
            string currentUser = (web.CurrentUser != null) ? web.CurrentUser.ID.ToString(CultureInfo.InvariantCulture) : string.Empty;
            // string isCheckedOutToLocal =  (string)file.Item["IsCheckedoutToLocal"],
            string isCheckedOutToLocal = "0";  // Drop box doesn't support check in/out.
            string permMask = file.PermMask;

            /*
            SPFieldLookupValue sPFieldLookupValue = file.Item["CheckedOutUserId"] as SPFieldLookupValue;
            string scriptLiteralToEncode = (sPFieldLookupValue == null) ? string.Empty : sPFieldLookupValue.LookupValue;
            scriptLiteralToEncode = SPHttpUtility.EcmaScriptStringLiteralEncode(scriptLiteralToEncode);
            */
            string scriptLiteralToEncode = string.Empty;  // Drop box doesn't support check in/out.

            return string.Format(CultureInfo.InvariantCulture, "DispEx(this,event,'{0}','{1}','{2}','{3}','{4}','{5}','{6}','{7}','{8}','{9}','{10}','{11}','{12}');", new object[]
            {
                "TRUE",
                "FALSE",
                "TRUE",
                text,
                openInBrowser,
                text,
                string.Empty,
                string.Empty,
                scriptLiteralToEncode,
                currentUser,
                forceCheckout,
                isCheckedOutToLocal,
                permMask
            });

        }

        /// <summary>
        /// Truncate the time part and the '/' from the Created Date and convert it to string to be a valid folder name.
        /// </summary>
        /// <param name="fullDate">The date as it is returned from the CreatedDate property of the assignment</param>
        /// <returns> The date converted to string and without the time and the '/' character </returns>
        static string GetDateOnly(DateTime fullDate)
        {
            return fullDate.ToString("yyyy-MM-dd");
        }

        static bool MustCopyFileToDropBox(string fileName)
        {
            return AssignmentFile.MustCopyFileToDropBox(Path.GetExtension(fileName));
        }
#endregion static methods
    }

#region AssignmentUpload
    /// <summary>An uploaded file for an assignment.</summary>
    public struct AssignmentUpload
    {
        string name;
        Stream stream;
        /// <summary>The file's contents.</summary>
        public Stream Stream
        {
            get { return stream ;}
        }
        /// <summary>The name of the file.</summary>
        public string Name
        {
            get { return name ;}
        }
        /// <summary>Initializes a new instance of <see cref="AssignmentUpload"/>.</summary>
        public AssignmentUpload(string name, Stream inputStream)
        {
            this.name = name;
            this.stream = inputStream;
        }
    }
#endregion AssignmentUpload
}
