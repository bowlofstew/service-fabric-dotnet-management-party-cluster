// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace WebService.Resources
{
    using System.Globalization;
    using System.Reflection;
    using System.Resources;

    /// <summary>
    /// Helper class for managing a RESX file.
    /// </summary>
    internal class Resx
    {
        private static ResourceManager resources;

        private static int count;

        public Resx(string name)
        {
            if (resources == null)
            {
                resources = new ResourceManager(name, Assembly.GetExecutingAssembly());

                ResourceSet set = resources.GetResourceSet(CultureInfo.CurrentUICulture, true, true);
                int total = 0;
                foreach (var item in set)
                {
                    ++total;
                }

                count = total;
            }
        }

        /// <summary>
        /// Gets the number of entries in the RESX file.
        /// </summary>
        public int Count { get { return count; } }

        /// <summary>
        /// Gets a ResourceManager for the RESX file.
        /// </summary>
        public ResourceManager Manager { get { return resources; } }
    }
}
