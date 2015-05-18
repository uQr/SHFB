//===============================================================================================================
// System  : Sandcastle Help File Builder Utilities
// File    : ConceptualContent.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 05/13/2015
// Note    : Copyright 2008-2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the class used to hold the conceptual content for a project during a build
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 04/24/2008  EFW  Created the code
// 06/06/2010  EFW  Added support for multi-format build output
// 04/03/2013  EFW  Added support for merging content from another project (plug-in support)
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml;

using Microsoft.Build.Evaluation;

using SandcastleBuilder.Utils.BuildEngine;

namespace SandcastleBuilder.Utils.ConceptualContent
{
    /// <summary>
    /// This class is used to hold the conceptual content settings for a project during a build
    /// </summary>
    public class ConceptualContentSettings
    {
        #region Private data members
        //=====================================================================

        private List<ImageReference> imageFiles;
        private List<ContentFile> codeSnippetFiles, tokenFiles, contentLayoutFiles;
        private Collection<TopicCollection> topics;
        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This is used to get the conceptual content image files
        /// </summary>
        public IList<ImageReference> ImageFiles
        {
            get { return imageFiles; }
        }

        /// <summary>
        /// This is used to get the conceptual content code snippet files
        /// </summary>
        public IList<ContentFile> CodeSnippetFiles
        {
            get { return codeSnippetFiles; }
        }

        /// <summary>
        /// This is used to get the conceptual content token files
        /// </summary>
        public IList<ContentFile> TokenFiles
        {
            get { return tokenFiles; }
        }

        /// <summary>
        /// This is used to get the conceptual content layout files
        /// </summary>
        public IList<ContentFile> ContentLayoutFiles
        {
            get { return contentLayoutFiles; }
        }

        /// <summary>
        /// This is used to get a collection of the conceptual content topics
        /// </summary>
        /// <remarks>Each item in the collection represents one content layout file from the project</remarks>
        public Collection<TopicCollection> Topics
        {
            get { return topics; }
        }
        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="project">The project from which to load the settings</param>
        public ConceptualContentSettings(SandcastleProject project)
        {
            imageFiles = project.ImagesReferences.ToList();
            codeSnippetFiles = project.ContentFiles(BuildAction.CodeSnippets).OrderBy(f => f.LinkPath).ToList();
            tokenFiles = project.ContentFiles(BuildAction.Tokens).OrderBy(f => f.LinkPath).ToList();
            contentLayoutFiles = project.ContentFiles(BuildAction.ContentLayout).ToList();
            topics = new Collection<TopicCollection>();

            var clf = new FileItemCollection(project, BuildAction.ContentLayout);

            foreach(FileItem file in clf)
                topics.Add(new TopicCollection(file));
        }
        #endregion

        #region Build process methods
        //=====================================================================

        /// <summary>
        /// This is used to copy the additional content token, image, and topic files to the build folder
        /// </summary>
        /// <param name="builder">The build process</param>
        /// <remarks>This will copy the code snippet file if specified, save token information to a shared
        /// content file called <b>_Tokens_.xml</b> in the build process's working folder, copy the image files
        /// to the <b>.\media</b> folder in the build process's working folder, save the media map to a file
        /// called <b>_MediaContent_.xml</b> in the build process's working folder, and save the topic files to
        /// the <b>.\ddueXml</b> folder in the build process's working folder.  The topic files will have their
        /// content wrapped in a <c>&lt;topic&gt;</c> tag if needed and will be named using their
        /// <see cref="Topic.Id" /> value.</remarks>
        public void CopyContentFiles(BuildProcess builder)
        {
            string folder;
            bool missingFile = false;

            builder.ReportProgress("Copying standard token shared content file...");
            builder.TransformTemplate("HelpFileBuilderTokens.tokens", builder.TemplateFolder,
                builder.WorkingFolder);

            builder.ReportProgress("Checking for other token files...");

            foreach(var tokenFile in this.tokenFiles)
                if(!File.Exists(tokenFile.FullPath))
                {
                    missingFile = true;
                    builder.ReportProgress("    Missing token file: {0}", tokenFile.FullPath);
                }
                else
                {
                    builder.ReportProgress("    {0} -> {1}", tokenFile.FullPath,
                        Path.Combine(builder.WorkingFolder, Path.GetFileName(tokenFile.FullPath)));
                    builder.TransformTemplate(Path.GetFileName(tokenFile.FullPath),
                        Path.GetDirectoryName(tokenFile.FullPath), builder.WorkingFolder);
                }

            if(missingFile)
                throw new BuilderException("BE0052", "One or more token files could not be found");

            builder.ReportProgress("Checking for code snippets files...");

            foreach(var snippetsFile in this.codeSnippetFiles)
                if(!File.Exists(snippetsFile.FullPath))
                {
                    missingFile = true;
                    builder.ReportProgress("    Missing code snippets file: {0}", snippetsFile.FullPath);
                }
                else
                    builder.ReportProgress("    Found {0}", snippetsFile.FullPath);

            if(missingFile)
                throw new BuilderException("BE0053", "One or more code snippets files could not be found");

            // Save the image info to a shared content file and copy the image files to the working folder
            folder = builder.WorkingFolder + "media";

            if(!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            // Create the build process's help format output folders too if needed
            builder.EnsureOutputFoldersExist("media");

            builder.ReportProgress("Copying images and creating the media map file...");

            // Copy all image project items and create the content file
            this.SaveImageSharedContent(builder.WorkingFolder + "_MediaContent_.xml", folder, builder);

            // Copy the topic files
            folder = builder.WorkingFolder + "ddueXml";

            if(!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            builder.ReportProgress("Generating conceptual topic files");

            // Create topic files
            foreach(TopicCollection tc in topics)
            {
                tc.Load();
                tc.GenerateConceptualTopics(folder, builder);
            }
        }

        /// <summary>
        /// Write the image reference collection to a map file ready for use by <strong>BuildAssembler</strong>
        /// </summary>
        /// <param name="filename">The file to which the image reference collection is saved</param>
        /// <param name="imagePath">The path to which the image files should be copied</param>
        /// <param name="builder">The build process</param>
        /// <remarks>Images with their <see cref="ImageReference.CopyToMedia" /> property set to true are copied
        /// to the media folder immediately.</remarks>
        public void SaveImageSharedContent(string filename, string imagePath, BuildProcess builder)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            XmlWriter writer = null;
            string destFile;

            builder.EnsureOutputFoldersExist("media");

            try
            {
                settings.Indent = true;
                settings.CloseOutput = true;
                writer = XmlWriter.Create(filename, settings);

                writer.WriteStartDocument();

                // There are normally some attributes on this element but they aren't used by Sandcastle so we'll
                // ignore them.
                writer.WriteStartElement("stockSharedContentDefinitions");

                foreach(var ir in imageFiles)
                {
                    writer.WriteStartElement("item");
                    writer.WriteAttributeString("id", ir.Id);
                    writer.WriteStartElement("image");

                    // The art build component assumes everything is in a single, common folder.  The build
                    // process will ensure that happens.  As such, we only need the filename here.
                    writer.WriteAttributeString("file", ir.Filename);

                    if(!String.IsNullOrEmpty(ir.AlternateText))
                    {
                        writer.WriteStartElement("altText");
                        writer.WriteValue(ir.AlternateText);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();   // </image>
                    writer.WriteEndElement();   // </item>

                    destFile = Path.Combine(imagePath, ir.Filename);

                    if(File.Exists(destFile))
                        builder.ReportWarning("BE0010", "Image file '{0}' already exists.  It will be replaced " +
                            "by '{1}'.", destFile, ir.FullPath);

                    builder.ReportProgress("    {0} -> {1}", ir.FullPath, destFile);

                    // The attributes are set to Normal so that it can be deleted after the build
                    File.Copy(ir.FullPath, destFile, true);
                    File.SetAttributes(destFile, FileAttributes.Normal);

                    // Copy it to the output media folders if CopyToMedia is true
                    if(ir.CopyToMedia)
                        foreach(string baseFolder in builder.HelpFormatOutputFolders)
                        {
                            destFile = Path.Combine(baseFolder + "media", ir.Filename);

                            builder.ReportProgress("    {0} -> {1} (Always copied)", ir.FullPath, destFile);

                            File.Copy(ir.FullPath, destFile, true);
                            File.SetAttributes(destFile, FileAttributes.Normal);
                        }
                }

                writer.WriteEndElement();   // </stockSharedContentDefinitions>
                writer.WriteEndDocument();
            }
            finally
            {
                if(writer != null)
                    writer.Close();
            }
        }

        /// <summary>
        /// This is used to create the conceptual content build configuration files
        /// </summary>
        /// <param name="builder">The build process</param>
        /// <remarks>This will create the companion files used to resolve conceptual links and the
        /// <b>_ContentMetadata_.xml</b> and <b>ConceptualManifest.xml</b> configuration files.</remarks>
        public void CreateConfigurationFiles(BuildProcess builder)
        {
            this.CreateCompanionFiles(builder);
            this.CreateContentMetadata(builder);
            this.CreateConceptualManifest(builder);
        }

        // TODO: This can eventually go away as the collections will be exposed as IList<T> properties.
        // TopicCollection needs some work first since the topics use FileItem instances.
        /// <summary>
        /// This method can be used by plug-ins to merge content from another Sandcastle Help File Builder
        /// project file.
        /// </summary>
        /// <param name="project">The project file from which to merge content</param>
        /// <remarks>Auto-generated content can be added to a temporary SHFB project and then added to the
        /// current project's content at build time using this method.  Such content cannot always be added to
        /// the project being built as it may alter the underlying MSBuild project which is not wanted.</remarks>
        public void MergeContentFrom(SandcastleProject project)
        {
            imageFiles.AddRange(project.ImagesReferences);
            codeSnippetFiles.AddRange(project.ContentFiles(BuildAction.CodeSnippets).OrderBy(f => f.LinkPath));
            tokenFiles.AddRange(project.ContentFiles(BuildAction.Tokens).OrderBy(f => f.LinkPath));
            contentLayoutFiles.AddRange(project.ContentFiles(BuildAction.ContentLayout));

            var clf = new FileItemCollection(project, BuildAction.ContentLayout);

            foreach(FileItem file in clf)
                topics.Add(new TopicCollection(file));
        }

        /// <summary>
        /// This is used to create the companion files used to resolve conceptual links
        /// </summary>
        /// <param name="builder">The build process</param>
        private void CreateCompanionFiles(BuildProcess builder)
        {
            string destFolder = builder.WorkingFolder + "xmlComp\\";

            builder.ReportProgress("    Companion files");

            if(!Directory.Exists(destFolder))
                Directory.CreateDirectory(destFolder);

            foreach(TopicCollection tc in topics)
                foreach(Topic t in tc)
                    t.WriteCompanionFile(destFolder, builder);
        }

        /// <summary>
        /// Create the content metadata file
        /// </summary>
        /// <param name="builder">The build process</param>
        /// <remarks>The content metadata file contains metadata information for each topic such as its title,
        /// table of contents title, help attributes, and index keywords.  Help attributes are a combination
        /// of the project-level help attributes and any parsed from the topic file.  Any replacement tags in
        /// the token values will be replaced with the appropriate project values.
        /// 
        /// <p/>A true MAML version of this file contains several extra attributes.  Since Sandcastle doesn't use
        /// them, I'm not going to waste time adding them.  The only stuff written is what is required by
        /// Sandcastle.  In addition, I'm putting the <c>title</c> and <c>PBM_FileVersion</c> item elements in
        /// here rather than use the separate companion files.  They all end up in the metadata section of the
        /// topic being built so this saves having two extra components in the configuration that do the same
        /// thing with different files.</remarks>
        private void CreateContentMetadata(BuildProcess builder)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            XmlWriter writer = null;

            builder.ReportProgress("    _ContentMetadata_.xml");

            try
            {
                settings.Indent = true;
                settings.CloseOutput = true;
                writer = XmlWriter.Create(builder.WorkingFolder + "_ContentMetadata_.xml", settings);

                writer.WriteStartDocument();
                writer.WriteStartElement("metadata");

                // Write out each topic and all of its sub-topics
                foreach(TopicCollection tc in topics)
                    foreach(Topic t in tc)
                        t.WriteMetadata(writer, builder);

                writer.WriteEndElement();   // </metadata>
                writer.WriteEndDocument();
            }
            finally
            {
                if(writer != null)
                    writer.Close();
            }
        }

        /// <summary>
        /// Create the content metadata file
        /// </summary>
        /// <param name="builder">The build process</param>
        /// <remarks>The content metadata file contains metadata information for each topic such as its title,
        /// table of contents title, help attributes, and index keywords.  Help attributes are a combination of
        /// the project-level help attributes and any parsed from the topic file.  Any replacement tags in the
        /// token values will be replaced with the appropriate project values.</remarks>
        private void CreateConceptualManifest(BuildProcess builder)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            XmlWriter writer = null;

            builder.ReportProgress("    ConceptualManifest.xml");

            try
            {
                settings.Indent = true;
                settings.CloseOutput = true;
                writer = XmlWriter.Create(builder.WorkingFolder + "ConceptualManifest.xml", settings);

                writer.WriteStartDocument();
                writer.WriteStartElement("topics");

                foreach(TopicCollection tc in topics)
                    foreach(Topic t in tc)
                        t.WriteManifest(writer, builder);

                writer.WriteEndElement();   // </topics>
                writer.WriteEndDocument();
            }
            finally
            {
                if(writer != null)
                    writer.Close();
            }
        }
        #endregion
    }
}
