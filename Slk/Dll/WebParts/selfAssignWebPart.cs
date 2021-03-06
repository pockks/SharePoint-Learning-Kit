using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls.WebParts;
using Microsoft.SharePoint;
using Microsoft.SharePoint.WebControls;
using Resources.Properties;

namespace Microsoft.SharePointLearningKit.WebParts
{
    /// <summary>A web part to show documents which can be self-assigned.</summary>
    public class SelfAssignWebPart : WebPart
    {
        List<string> errors = new List<string>();
        SlkCulture culture = new SlkCulture();

#region constructors
#endregion constructors

#region properties
        ///<summary>The site to get the documents from.</summary>
        [WebBrowsable(),
         AlwpWebDisplayName("SelfAssignSiteUrlDisplayName"),
         AlwpWebDescription("SelfAssignSiteUrlDescription"),
         SlkCategory(),
         Personalizable(PersonalizationScope.Shared)]
        public string SiteUrl { get; set; }

        ///<summary>The list to get the documents from.</summary>
        [WebBrowsable(),
         AlwpWebDisplayName("SelfAssignListNameDisplayName"),
         AlwpWebDescription("SelfAssignListNameDescription"),
         SlkCategory(),
         Personalizable(PersonalizationScope.Shared)]
        public string ListName { get; set; }

        ///<summary>The view to get the documents from.</summary>
        [WebBrowsable(),
         AlwpWebDisplayName("SelfAssignViewNameDisplayName"),
         AlwpWebDescription("SelfAssignViewNameDescription"),
         SlkCategory(),
         Personalizable(PersonalizationScope.Shared)]
        public string ViewName { get; set; }

        /// <summary>See <see cref="WebPart.Description"/>.</summary>
        public override string Description
        {
            get
            {
                // Localise the description if empty or it is the default value
                if (string.IsNullOrEmpty(base.Description) || base.Description == GetLocalizedString("SelfAssignWebPartDescription", CultureInfo.InvariantCulture.LCID))
                {
                    return GetLocalizedString("SelfAssignWebPartDescription", culture.Culture.LCID);
                }
                else
                {
                    return base.Description;
                }
            }

            set
            {
                if (value == GetLocalizedString("SelfAssignWebPartDescription", CultureInfo.InvariantCulture.LCID) 
                        || value == GetLocalizedString("SelfAssignWebPartDescription", CultureInfo.InvariantCulture.LCID))
                {
                    base.Title = null;
                }
                else
                {
                    base.Title = value;
                }
            }
        }

        /// <summary>See <see cref="WebPart.Title"/>.</summary>
        public override string Title
        {
            get
            {
                // Localise the description if empty or it is the default value
                if (string.IsNullOrEmpty(base.Title) || base.Title == GetLocalizedString("SelfAssignWebPartTitle", CultureInfo.InvariantCulture.LCID))
                {
                    return GetLocalizedString("SelfAssignWebPartTitle", culture.Culture.LCID);
                }
                else
                {
                    return base.Title;
                }
            }

            set
            {
                if (value == GetLocalizedString("SelfAssignWebPartTitle", CultureInfo.InvariantCulture.LCID) 
                        || value == GetLocalizedString("SelfAssignWebPartTitle", CultureInfo.InvariantCulture.LCID))
                {
                    base.Title = null;
                }
                else
                {
                    base.Title = value;
                }
            }
        }

#endregion properties

#region public methods
#endregion public methods

#region protected methods
        /// <summary>Renders the web part.</summary>
        protected override void RenderContents(HtmlTextWriter writer)
        {
            if (ValidateProperties())
            {
                SPListItemCollection items = FindDocuments();

                if (items != null && items.Count > 0)
                {
                    writer.Write("<ul class=\"slk-sa\">");

                    foreach (SPListItem item in items)
                    {
                        if (item.File != null)
                        {
                            string url = "{0}{1}frameset/frameset.aspx?ListId={2}&ItemId={3}&SlkView=Execute&play=true";
                            string serverRelativeUrl = SPControl.GetContextWeb(Context).ServerRelativeUrl;
                            url = string.Format(CultureInfo.InvariantCulture, url, serverRelativeUrl, Constants.SlkUrlPath, item.ParentList.ID.ToString("B"), item.ID);

                            string title = item.Title;
                            if (string.IsNullOrEmpty(title))
                            {
                                title = item.Name;
                            }

                            writer.Write("<li><a href=\"{0}\" target=\"_blank\">{1}</a></li>", url, HttpUtility.HtmlEncode(title));
                        }
                    }

                    writer.Write("</ul>");
                }
                else
                {
                    writer.Write("<p class=\"ms-vb\">{0}</p>", HttpUtility.HtmlEncode(culture.Resources.SelfAssignPartNoItems));
                }
            }

            RenderErrors(writer);

        }
#endregion protected methods

#region private methods
        private string GetLocalizedString(string resourceName, int lcid)
        {
            if (string.IsNullOrEmpty(resourceName))
            {
                return string.Empty;
            }
            else
            {
                string resourceFile = "SLK";
                return Microsoft.SharePoint.Utilities.SPUtility.GetLocalizedString("$Resources:" + resourceName, resourceFile, (uint)lcid);
            }
        }

        bool ValidateProperties()
        {
            if (string.IsNullOrEmpty(ListName))
            {
                errors.Add(culture.Resources.SelfAssignListNameRequired);
                return false;
            }

            return true;
        }

        void RenderErrors(HtmlTextWriter writer)
        {
            if (errors.Count > 0)
            {
                writer.Write("<p class=\"ms-formvalidation\">");

                foreach (string error in errors)
                {
                    writer.Write(HttpUtility.HtmlEncode(error));
                    writer.Write("<br />");
                }

                writer.Write("</p>");
            }
        }

        SPListItemCollection FindDocuments()
        {
            if (string.IsNullOrEmpty(SiteUrl))
            {
                return FindDocuments(SPContext.Current.Web);
            }
            else
            {
                using (SPSite site = new SPSite(SiteUrl))
                {
                    using (SPWeb web = site.OpenWeb())
                    {
                        return FindDocuments(web);
                    }
                }
            }
        }

        SPListItemCollection FindDocuments(SPWeb web)
        {
            SPList list;
            try
            {
                list = web.Lists[ListName];
            }
            catch (SPException)
            {
                errors.Add(culture.Resources.SelfAssignInvalidList);
                return null;
            }

            SPView view;

            if (string.IsNullOrEmpty(ViewName))
            {
                view = list.DefaultView;
            }
            else
            {
                try
                {
                    view = list.Views[ViewName];
                }
                catch (ArgumentException)
                {
                    errors.Add(culture.Resources.SelfAssignInvalidView);
                    return null;
                }
                catch (SPException)
                {
                    errors.Add(culture.Resources.SelfAssignInvalidView);
                    return null;
                }
            }

            SPQuery query = new SPQuery();
            query.Query = view.Query;
            if (view.Scope == SPViewScope.Recursive || view.Scope == SPViewScope.RecursiveAll)
            {
                query.ViewAttributes = "Scope=\"Recursive\"";
            }

            // Cannot use list.GetItems(view) as that limits the columns and may not include the title column
            return list.GetItems(query);
        }
#endregion private methods

#region static members
#endregion static members
    }
}

