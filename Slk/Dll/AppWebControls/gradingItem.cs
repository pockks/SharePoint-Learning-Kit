using System;
using System.Configuration;
using System.Web;
using System.Web.Security;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.Web.UI.WebControls.WebParts;
using System.Web.UI.HtmlControls;
using Microsoft.SharePointLearningKit;
using System.Collections.Generic;
using System.Collections.Specialized;
using Microsoft.LearningComponents;
using Microsoft.LearningComponents.Frameset;
using Resources.Properties;
using System.Text;
using System.Globalization;
using Microsoft.SharePointLearningKit.ApplicationPages;
using Microsoft.SharePoint;

namespace Microsoft.SharePointLearningKit.WebControls
{
    /// <summary>
    /// Represents a data item in a Grading list control. 
    /// captures grades and comments  and other related information 
    /// for each learner assignment.
    /// This class cannot be inherited.  
    /// </summary>
    internal sealed class GradingItem
    {

        #region Private Variables
        /// <summary>
        /// Holds Assignment ID
        /// </summary> 
        private long m_learnerId;
        /// <summary>
        /// Holds Assignment ID
        /// </summary> 
        private long m_learnerAssignmentId;
        /// <summary>
        /// Holds the Learner Assignment Guid ID
        /// </summary> 
        private Guid m_learnerAssignmentGuidId;
        /// <summary>
        /// Holds Learnerís name
        /// </summary> 
        private string m_learnerName;
        /// <summary>
        /// Holds the status field
        /// </summary>
        private LearnerAssignmentState m_status;
        /// <summary>
        /// Holds the results
        /// </summary>
        private SuccessStatus m_successStatus;
        /// <summary>
        /// Holds the computed points.
        /// </summary>    
        private float? m_gradedScore;
        /// <summary>
        /// Holds computed points value
        /// </summary>
        private float? m_finalScore;
        /// <summary>
        /// Holds InstructorComments Value
        /// </summary>
        private string m_instructorComments;
        /// <summary>
        /// Holds Action Text
        /// </summary>
        private string m_actionText;
        /// <summary>
        /// Holds Action State
        /// </summary>
        private bool m_actionState;
        /// <summary>
        /// Holds File Submission State
        /// </summary>
        private string m_fileSubmissionState;
        


        #endregion

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>

        public GradingItem()
        {
        }

        #endregion

        #region Public Properties
        /// <summary>
        /// Learner assignment ID
        /// </summary> 
        public long LearnerAssignmentId
        {
            get { return m_learnerAssignmentId; }
            set { m_learnerAssignmentId = value; }
        }
        /// <summary>
        /// Learner assignment Guid ID
        /// </summary> 
        public Guid LearnerAssignmentGuidId
        {
            get { return m_learnerAssignmentGuidId; }
            set { m_learnerAssignmentGuidId = value; }
        }
        /// <summary>
        /// Learner Guid ID
        /// </summary> 
        public long LearnerId
        {
            get { return m_learnerId; }
            set { m_learnerId = value; }
        }
        /// <summary>
        /// Learnerís name
        /// </summary> 
        public string LearnerName
        {
            get { return m_learnerName; }
            set { m_learnerName = value; }
        }
        /// <summary>
        /// Displays the status of the learner assignment
        /// </summary>
        public LearnerAssignmentState Status
        {
            get { return m_status; }
            set { m_status = value; }
        }
        /// <summary>
        /// Displays the SuccessStatus of the learner assignment
        /// </summary>
        public SuccessStatus SuccessStatus
        {
            get { return m_successStatus; }
            set { m_successStatus = value; }
        }
        /// <summary>
        /// Shows the computed points rouded to the nearest integer.
        /// </summary>
        public float? GradedScore
        {
            get { return m_gradedScore; }
            set { m_gradedScore = value; }
        }

        /// <summary>
        /// computed points value with full precision
        /// </summary>
        public float? FinalScore
        {
            get { return m_finalScore; }
            set { m_finalScore = value; }
        }

        public string Grade { get; set; }

        /// <summary>Comments from the Learner.</summary>
        public string LearnerComments { get; set; }

        /// <summary>
        /// InstructorComments
        /// </summary>
        public string InstructorComments
        {
            get { return m_instructorComments; }
            set { m_instructorComments = value; }
        }
        /// <summary>
        /// Action State
        /// </summary>
        public string ActionText
        {
            get { return m_actionText; }
            set { m_actionText = value; }
        }

        /// <summary>
        /// Action Checked
        /// </summary>
        public bool ActionState
        {
            get { return m_actionState; }
            set { m_actionState = value; }
        }

        /// <summary>
        /// File Submission State
        /// </summary>
        public string FileSubmissionState
        {
            get { return m_fileSubmissionState; }
            set { m_fileSubmissionState = value; }
        }

        /// <summary>Whether the item is complete or not.</summary>
        public bool IsComplete
        {
            get { return (Status == LearnerAssignmentState.Completed || Status == LearnerAssignmentState.Final) ;}
        }



        #endregion
    }

}
