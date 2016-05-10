// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Resources
{
    using System.Globalization;
    using System.Reflection;
    using System.Resources;

    /// <summary>
    /// Helper class for managing a RESX file.
    /// </summary>
    internal class Resx
    {
        public Resx(string name)
        {
            this.Manager = new ResourceManager(name, Assembly.GetExecutingAssembly());

            ResourceSet set = this.Manager.GetResourceSet(CultureInfo.CurrentUICulture, true, true);

            foreach (object item in set)
            {
                ++this.Count;
            }
        }

        /// <summary>
        /// Gets the number of entries in the RESX file.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Gets a ResourceManager for the RESX file.
        /// </summary>
        public ResourceManager Manager { get; }
    }
}