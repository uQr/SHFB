//===============================================================================================================
// System  : Sandcastle Help File Builder Utilities
// File    : SubstitutionTagReplacement.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 05/10/2015
// Note    : Copyright 2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the class used to handle substitution tag replacement in build template files
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 05/10/2015  EFW  Refactored the substitution tag replacement code and moved it into its own class
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

using Microsoft.Build.Evaluation;

using Sandcastle.Core;
using Sandcastle.Core.Frameworks;
using Sandcastle.Core.PresentationStyle;

using SandcastleBuilder.Utils.BuildComponent;
using SandcastleBuilder.Utils.Design;

namespace SandcastleBuilder.Utils.BuildEngine
{
    /// <summary>
    /// This class handles substitution tag replacement in build template files
    /// </summary>
    public class SubstitutionTagReplacement
    {
        #region Private data members
        //=====================================================================

        private BuildProcess currentBuild;
        private SandcastleProject sandcastleProject;
        private Project msbuildProject;
        private PresentationStyleSettings presentationStyle;

        private Dictionary<string, MethodInfo> methodCache;

        private static Regex reField = new Regex(@"{@(?<Field>\w*?)(:(?<Format>.*?))?}");

        private MatchEvaluator fieldMatchEval;
        private string fieldFormat;

        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="currentBuild">The current build for which to perform substitution tag replacement</param>
        public SubstitutionTagReplacement(BuildProcess currentBuild)
        {
            this.currentBuild = currentBuild;

            sandcastleProject = currentBuild.CurrentProject;
            msbuildProject = sandcastleProject.MSBuildProject;
            presentationStyle = currentBuild.PresentationStyle;

            fieldMatchEval = new MatchEvaluator(OnFieldMatch);

            // Get the substitution tag methods so that we can invoke them.  The dictionary keys are the method
            // names and are case-insensitive.  Substitution tag methods take no parameters and return a value
            // that is convertible to a string.
            methodCache = this.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Where(
                m => m.GetCustomAttribute(typeof(SubstitutionTagAttribute)) != null).ToDictionary(
                m => m.Name, m => m, StringComparer.OrdinalIgnoreCase);
        }
        #endregion

        #region Transformation methods
        //=====================================================================

        /// <summary>
        /// Transform the specified template text by replacing the substitution tags with the corresponding
        /// project property values.
        /// </summary>
        /// <param name="templateText">The template text to transform</param>
        /// <param name="args">An optional list of arguments to format into the  template before transforming it</param>
        /// <returns>The transformed text</returns>
        public string TransformText(string templateText, params object[] args)
        {
            if(String.IsNullOrWhiteSpace(templateText))
                return (templateText ?? String.Empty);

            if(args.Length != 0)
                templateText = String.Format(CultureInfo.InvariantCulture, templateText, args);

            try
            {
                // Find and replace all substitution tags with a matching value from the project.  They can be
                // nested.
                while(reField.IsMatch(templateText))
                    templateText = reField.Replace(templateText, fieldMatchEval);
            }
            catch(BuilderException)
            {
                throw;
            }
            catch(Exception ex)
            {
                throw new BuilderException("BE0018", String.Format(CultureInfo.CurrentCulture,
                    "Unable to transform template text '{0}': {1}", templateText, ex.Message), ex);
            }

            return templateText;
        }

        /// <summary>
        /// Transform the specified template file by inserting the necessary values into the substitution tags
        /// and saving it to the destination folder.
        /// </summary>
        /// <param name="templateFile">The template file to transform</param>
        /// <param name="sourceFolder">The folder where the template is located</param>
        /// <param name="destFolder">The folder in which to save the transformed file</param>
        /// <returns>The path to the transformed file</returns>
        public string TransformTemplate(string templateFile, string sourceFolder, string destFolder)
        {
            Encoding enc = Encoding.Default;
            string templateText, transformedFile;

            if(templateFile == null)
                throw new ArgumentNullException("template");

            if(sourceFolder == null)
                throw new ArgumentNullException("sourceFolder");

            if(destFolder == null)
                throw new ArgumentNullException("destFolder");

            if(sourceFolder.Length != 0 && sourceFolder[sourceFolder.Length - 1] != '\\')
                sourceFolder += @"\";

            if(destFolder.Length != 0 && destFolder[destFolder.Length - 1] != '\\')
                destFolder += @"\";

            try
            {
                // When reading the file, use the default encoding but detect the encoding if byte order marks
                // are present.
                templateText = Utility.ReadWithEncoding(sourceFolder + templateFile, ref enc);

                // Find and replace all substitution tags with a matching value from the project.  They can be
                // nested.
                while(reField.IsMatch(templateText))
                    templateText = reField.Replace(templateText, fieldMatchEval);

                transformedFile = destFolder + templateFile;

                // Write the file back out using its original encoding
                using(StreamWriter sw = new StreamWriter(transformedFile, false, enc))
                {
                    sw.Write(templateText);
                }
            }
            catch(BuilderException)
            {
                throw;
            }
            catch(Exception ex)
            {
                throw new BuilderException("BE0019", String.Format(CultureInfo.CurrentCulture,
                    "Unable to transform template '{0}': {1}", templateFile, ex.Message), ex);
            }

            return transformedFile;
        }

        // TODO: Make this private once BuildProcess.OnFieldMatch is removed
        /// <summary>
        /// Replace a substitution tag with a value from the project
        /// </summary>
        /// <param name="match">The match that was found</param>
        /// <returns>The string to use as the replacement</returns>
        internal string OnFieldMatch(Match match)
        {
            MethodInfo method;
            string fieldName = match.Groups["Field"].Value, propertyValue;

            // See if a method exists first.  If so, we'll call it and return its value
            if(methodCache.TryGetValue(fieldName, out method))
            {
                fieldFormat = match.Groups["Format"].Value;

                object result = method.Invoke(this, null);

                if(result == null)
                    return String.Empty;

                return result.ToString();
            }

            // Try for a project property.  Use the last one since the original may be in a parent project file
            // or it may have been overridden from the command line.
            var buildProp = msbuildProject.AllEvaluatedProperties.LastOrDefault(
                p => p.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            if(buildProp != null)
                return buildProp.EvaluatedValue;

            // If not there, try the global properties.  If still not found, give up.
            string key = msbuildProject.GlobalProperties.Keys.FirstOrDefault(
                k => k.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            if(key == null || !msbuildProject.GlobalProperties.TryGetValue(key, out propertyValue))
                switch(fieldName.ToUpperInvariant())
                {
                    case "REFERENCEPATH":       // These can be safely ignored if not found
                    case "OUTDIR":
                        propertyValue = String.Empty;
                        break;

                    default:
                        throw new BuilderException("BE0020", String.Format(CultureInfo.CurrentCulture,
                            "Unknown substitution tag ID: '{0}'", fieldName));
                }

            return propertyValue;
        }
        #endregion

        #region Project and build folder substitution tags
        //=====================================================================

        /// <summary>
        /// The application data folder
        /// </summary>
        /// <returns>The application data folder.  This folder should exist if used.</returns>
        [SubstitutionTag]
        private string AppDataFolder()
        {
            return FolderPath.TerminatePath(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData), Constants.ProgramDataFolder));
        }

        /// <summary>
        /// The local data folder
        /// </summary>
        /// <returns>The local data folder.  This folder may not exist and we may need to create it.</returns>
        [SubstitutionTag]
        private string LocalDataFolder()
        {
            string folder = FolderPath.TerminatePath(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), Constants.ProgramDataFolder));

            if(!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            return folder;
        }

        /// <summary>
        /// The help file builder folder
        /// </summary>
        /// <returns>The help file builder folder</returns>
        [SubstitutionTag]
        private string SHFBFolder()
        {
            return ComponentUtilities.ToolsFolder;
        }

        /// <summary>
        /// The components folder
        /// </summary>
        /// <returns>The components folder</returns>
        [SubstitutionTag]
        private string ComponentsFolder()
        {
            return ComponentUtilities.ComponentsFolder;
        }

        /// <summary>
        /// The current build's help file builder project folder
        /// </summary>
        /// <returns>The current build's help file builder project folder</returns>
        [SubstitutionTag]
        private string ProjectFolder()
        {
            return currentBuild.ProjectFolder;
        }

        /// <summary>
        /// The current build's HTML encoded help file builder project folder
        /// </summary>
        /// <returns>The current build's HTML encoded help file builder project folder</returns>
        [SubstitutionTag]
        private string HtmlEncProjectFolder()
        {
            return WebUtility.HtmlEncode(currentBuild.ProjectFolder);
        }

        /// <summary>
        /// The current build's output folder
        /// </summary>
        /// <returns>The current build's output folder</returns>
        [SubstitutionTag]
        private string OutputFolder()
        {
            return currentBuild.OutputFolder;
        }

        /// <summary>
        /// The current build's HTML encoded output folder
        /// </summary>
        /// <returns>The current build's HTML encoded output folder</returns>
        [SubstitutionTag]
        private string HtmlEncOutputFolder()
        {
            return WebUtility.HtmlEncode(currentBuild.OutputFolder);
        }

        /// <summary>
        /// The current build's working folder
        /// </summary>
        /// <returns>The current build's working folder</returns>
        [SubstitutionTag]
        private string WorkingFolder()
        {
            return currentBuild.WorkingFolder;
        }

        /// <summary>
        /// The current build's HTML encoded working folder
        /// </summary>
        /// <returns>The current build's HTML encoded working folder</returns>
        [SubstitutionTag]
        private string HtmlEncWorkingFolder()
        {
            return WebUtility.HtmlEncode(currentBuild.WorkingFolder);
        }

        /// <summary>
        /// The HTML Help 1 compiler path
        /// </summary>
        /// <returns>The HTML Help 1 compiler path</returns>
        [SubstitutionTag]
        private string HHCPath()
        {
            return currentBuild.Help1CompilerFolder;
        }
        #endregion

        #region Presentation style properties
        //=====================================================================

        /// <summary>
        /// The presentation style
        /// </summary>
        /// <returns>The presentation style</returns>
        [SubstitutionTag]
        private string PresentationStyle()
        {
            return sandcastleProject.PresentationStyle;
        }

        /// <summary>
        /// The presentation style folder
        /// </summary>
        /// <returns>The presentation style folder</returns>
        [SubstitutionTag]
        private string PresentationPath()
        {
            return FolderPath.TerminatePath(currentBuild.PresentationStyleFolder);
        }

        /// <summary>
        /// The document model XSL transformation filename
        /// </summary>
        /// <returns>The document model XSL transformation filename</returns>
        [SubstitutionTag]
        private string DocModelTransformation()
        {
            return presentationStyle.ResolvePath(presentationStyle.DocumentModelTransformation.TransformationFilename);
        }

        /// <summary>
        /// The document model XSL transformation parameters
        /// </summary>
        /// <returns>The document model XSL transformation parameters</returns>
        [SubstitutionTag]
        private string DocModelTransformationParameters()
        {
            return String.Join(";", presentationStyle.DocumentModelTransformation.Select(
                p => String.Format(CultureInfo.InvariantCulture, "{0}={1}", p.Key, p.Value)));
        }

        /// <summary>
        /// The intermediate TOC XSL transformation filename
        /// </summary>
        /// <returns>The intermediate TOC XSL transformation filename</returns>
        [SubstitutionTag]
        private string TocTransformation()
        {
            return presentationStyle.ResolvePath(presentationStyle.IntermediateTocTransformation.TransformationFilename);
        }

        /// <summary>
        /// The intermediate TOC XSL transformation parameters
        /// </summary>
        /// <returns>The intermediate TOC XSL transformation parameters</returns>
        [SubstitutionTag]
        private string TocTransformParameters()
        {
            return String.Join(";", presentationStyle.IntermediateTocTransformation.Select(
                p => String.Format(CultureInfo.InvariantCulture, "{0}={1}", p.Key, p.Value)));
        }
        #endregion

        #region General project properties
        //=====================================================================

        /// <summary>
        /// The naming method
        /// </summary>
        /// <returns>The naming method</returns>
        public string NamingMethod()
        {
            return sandcastleProject.NamingMethod.ToString();
        }
        #endregion
    }
}
