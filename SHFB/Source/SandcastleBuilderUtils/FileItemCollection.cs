//===============================================================================================================
// System  : Sandcastle Help File Builder Utilities
// File    : FileItemCollection.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 05/13/2015
// Note    : Copyright 2008-2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a collection class used to hold build items from the project
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/07/2008  EFW  Created the code
// 07/09/2010  EFW  Updated for use with .NET 4.0 and MSBuild 4.0.
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;

using Microsoft.Build.Evaluation;

namespace SandcastleBuilder.Utils
{
    /// <summary>
    /// This collection class is used to hold build items from a project.
    /// </summary>
    public class FileItemCollection : BindingList<FileItem>
    {
        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="project">The project file containing the build items</param>
        /// <param name="buildAction">The build action for the items to hold in the collection</param>
        public FileItemCollection(SandcastleProject project, BuildAction buildAction)
        {
            foreach(ProjectItem item in project.MSBuildProject.GetItems(buildAction.ToString()))
                this.Add(new FileItem(project, item));

            switch(buildAction)
            {
                case BuildAction.ContentLayout:
                case BuildAction.SiteMap:
                    ((List<FileItem>)base.Items).Sort((x, y) =>
                    {
                        if(x.SortOrder < y.SortOrder)
                            return -1;
                        
                        if(x.SortOrder > y.SortOrder)
                            return 1;
                        
                        return String.Compare(x.LinkPath.Path, y.LinkPath.Path, StringComparison.OrdinalIgnoreCase);
                    });
                    break;

                default:
                    ((List<FileItem>)base.Items).Sort((x, y) =>
                    {
                        return String.Compare(x.LinkPath.Path, y.LinkPath.Path, StringComparison.OrdinalIgnoreCase);
                    });
                    break;
            }
        }
        #endregion
    }
}
