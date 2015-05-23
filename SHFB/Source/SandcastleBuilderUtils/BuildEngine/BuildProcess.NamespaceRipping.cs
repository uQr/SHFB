//===============================================================================================================
// System  : Sandcastle Help File Builder Utilities
// File    : BuildProcess.NamespaceRipping.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 05/23/2015
// Note    : Copyright 2007-2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the code used to generate the API filter collection information used by MRefBuilder to
// exclude API entries while generating the reflection information file.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 07/16/2007  EFW  Created the code
// 09/13/2007  EFW  Added support for calling plug-ins
// 03/10/2008  EFW  Added support for applying the API filter manually
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;

using SandcastleBuilder.Utils.BuildComponent;

namespace SandcastleBuilder.Utils.BuildEngine
{
    partial class BuildProcess
    {
        #region Manual API filter methods
        //=====================================================================

        // TODO: Remove this section and put it in a plug-in

        /// <summary>
        /// This is used to manually apply the specified API filter to the specified reflection information file
        /// </summary>
        /// <param name="filterToApply">The API filter to apply</param>
        /// <param name="reflectionFilename">The reflection information file</param>
        /// <remarks>This can be used by any plug-in that does not produce a reflection information file using
        /// <b>MRefBuilder.exe</b>.  In such cases, the API filter is not applied unless the plug-in uses this
        /// method.  If the reflection information file is produced by <b>MRefBuilder.exe</b>, there is no need
        /// to use this method as it will apply the API filter automatically to the file that it produces.</remarks>
        public void ApplyManualApiFilter(ApiFilterCollection filterToApply, string reflectionFilename)
        {
            XmlDocument refInfo;
            XmlNode apis;
            string id;
            bool keep;

            refInfo = new XmlDocument();
            refInfo.Load(reflectionFilename);
            apis = refInfo.SelectSingleNode("reflection/apis");

            foreach(ApiFilter nsFilter in filterToApply)
                if(nsFilter.Children.Count == 0)
                    this.RemoveNamespace(apis, nsFilter.FullName);
                else
                    if(!nsFilter.IsExposed)
                    {
                        // Remove all but the indicated types
                        foreach(XmlNode typeNode in apis.SelectNodes("api[starts-with(@id, 'T:') and containers/" +
                          "namespace/@api='N:" + nsFilter.FullName + "']"))
                        {
                            id = typeNode.Attributes["id"].Value.Substring(2);
                            keep = false;

                            foreach(ApiFilter typeFilter in nsFilter.Children)
                                if(typeFilter.FullName == id)
                                {
                                    // Just keep or remove members
                                    this.ApplyMemberFilter(apis, typeFilter);
                                    keep = true;
                                    break;
                                }

                            if(!keep)
                                this.RemoveType(apis, id);
                        }
                    }
                    else
                    {
                        // Remove just the indicated types or their members
                        foreach(ApiFilter typeFilter in nsFilter.Children)
                        {
                            if(!typeFilter.IsExposed && typeFilter.Children.Count == 0)
                            {
                                this.RemoveType(apis, typeFilter.FullName);
                                continue;
                            }

                            // Just keep or remove members
                            this.ApplyMemberFilter(apis, typeFilter);
                        }
                    }

            refInfo.Save(reflectionFilename);
        }

        /// <summary>
        /// Apply a member filter to the specified type.
        /// </summary>
        /// <param name="apis">The APIs node from which to remove info</param>
        /// <param name="typeFilter">The type filter to be processed</param>
        private void ApplyMemberFilter(XmlNode apis, ApiFilter typeFilter)
        {
            string id;
            bool keep;
            int pos;

            if(!typeFilter.IsExposed)
            {
                // Remove all but the indicated members
                foreach(XmlNode memberNode in apis.SelectNodes("api[containers/type/@api='T:" +
                  typeFilter.FullName + "']"))
                {
                    id = memberNode.Attributes["id"].Value.Substring(2);
                    pos = id.IndexOf('(');
                    keep = false;

                    // The API filter ignores parameters on methods
                    if(pos != -1)
                        id = id.Substring(0, pos);

                    foreach(ApiFilter memberFilter in typeFilter.Children)
                        if(memberFilter.FullName == id)
                        {
                            keep = true;
                            break;
                        }

                    if(!keep)
                    {
                        id = memberNode.Attributes["id"].Value;
                        this.ReportProgress("    Removing member '{0}'", id);
                        memberNode.ParentNode.RemoveChild(memberNode);

                        // Remove the element nodes too
                        foreach(XmlNode element in apis.SelectNodes("api/elements/element[@api='" + id + "']"))
                            element.ParentNode.RemoveChild(element);
                    }
                }
            }
            else
            {
                // Remove just the indicated members
                foreach(ApiFilter memberFilter in typeFilter.Children)
                    foreach(XmlNode memberNode in apis.SelectNodes("api[starts-with(substring-after(@id,':'),'" +
                      memberFilter.FullName + "')]"))
                    {
                        id = memberNode.Attributes["id"].Value.Substring(2);
                        pos = id.IndexOf('(');

                        // The API filter ignores parameters on methods
                        if(pos != -1)
                            id = id.Substring(0, pos);

                        if(id == memberFilter.FullName)
                        {
                            id = memberNode.Attributes["id"].Value;
                            this.ReportProgress("    Removing member '{0}'", id);
                            memberNode.ParentNode.RemoveChild(memberNode);

                            // Remove the element nodes too
                            foreach(XmlNode element in apis.SelectNodes("api/elements/element[@api='" + id + "']"))
                                element.ParentNode.RemoveChild(element);
                        }
                    }
            }
        }

        /// <summary>
        /// Remove an entire namespace and all of its members
        /// </summary>
        /// <param name="apis">The APIs node from which to remove info</param>
        /// <param name="id">The namespace ID to remove</param>
        private void RemoveNamespace(XmlNode apis, string id)
        {
            XmlNode ns;
            string nodeId;

            this.ReportProgress("    Removing namespace 'N:{0}'", id);
            ns = apis.SelectSingleNode("api[@id='N:" + id + "']");

            if(ns != null)
            {
                // Remove the namespace container
                ns.ParentNode.RemoveChild(ns);

                // Remove all of the namespace members
                foreach(XmlNode xn in apis.SelectNodes("api[containers/namespace/@api='N:" + id + "']"))
                {
                    xn.ParentNode.RemoveChild(xn);

                    // Remove the element nodes too
                    nodeId = xn.Attributes["id"].Value;

                    foreach(XmlNode element in apis.SelectNodes("api/elements/element[@api='" + nodeId + "']"))
                        element.ParentNode.RemoveChild(element);
                }
            }
        }

        /// <summary>
        /// Remove an entire type and all of its members
        /// </summary>
        /// <param name="apis">The APIs node from which to remove info</param>
        /// <param name="id">The type ID to remove</param>
        private void RemoveType(XmlNode apis, string id)
        {
            XmlNode typeNode;
            string nodeId;

            this.ReportProgress("    Removing type 'T:{0}'", id);
            typeNode = apis.SelectSingleNode("api[@id='T:" + id + "']");

            if(typeNode != null)
            {
                // Remove the namespace container
                typeNode.ParentNode.RemoveChild(typeNode);

                // Remove all of the type members
                foreach(XmlNode xn in apis.SelectNodes("api[containers/type/@api='T:" + id + "']"))
                {
                    xn.ParentNode.RemoveChild(xn);

                    // Remove the element nodes too
                    nodeId = xn.Attributes["id"].Value;

                    foreach(XmlNode element in apis.SelectNodes("api/elements/element[@api='" + nodeId + "']"))
                        element.ParentNode.RemoveChild(element);
                }

                // Remove namespace element nodes
                foreach(XmlNode element in apis.SelectNodes("api/elements/element[@api='T:" + id + "']"))
                    element.ParentNode.RemoveChild(element);
            }
        }
        #endregion
    }
}
