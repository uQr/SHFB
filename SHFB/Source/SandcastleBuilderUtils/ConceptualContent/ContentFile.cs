//===============================================================================================================
// System  : Sandcastle Help File Builder Utilities
// File    : ContentFile.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 05/13/2015
// Note    : Copyright 2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class representing a content file such as a token file, code snippet file, etc.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 05/13/2015  EFW  Created the code
//===============================================================================================================

using System;
using System.IO;

namespace SandcastleBuilder.Utils.ConceptualContent
{
    /// <summary>
    /// This represents a conceptual content image that can be used to insert a reference to an image in a topic
    /// </summary>
    public class ContentFile
    {
        #region Properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get the content filename without the path
        /// </summary>
        public string Filename
        {
            get { return Path.GetFileName(this.FullPath); }
        }

        /// <summary>
        /// This is used to get or set the full path to the content file
        /// </summary>
        /// <remarks>This returns the path to the file's true location.  For linked items, this path will differ
        /// from the <see cref="LinkPath"/> with returns the project-relative location.</remarks>
        public string FullPath { get; set; }

        /// <summary>
        /// This is used to get or set the link path to the content file (the project-relative location)
        /// </summary>
        /// <remarks>For linked items, this will be the location of the file within the project.  For files that
        /// are part of the project, this will match the <see cref="FullPath"/> property value.</remarks>
        public string LinkPath { get; set; }

        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="fullPath">The full path to the content file</param>
        public ContentFile(string fullPath)
        {
            if(String.IsNullOrWhiteSpace(fullPath))
                throw new ArgumentException("A full path to the content file is required", "fullPath");

            this.FullPath = Path.GetFullPath(fullPath);
        }
        #endregion
    }
}
