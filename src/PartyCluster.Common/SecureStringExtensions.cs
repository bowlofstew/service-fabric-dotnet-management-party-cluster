// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Common
{
    using System;
    using System.Runtime.InteropServices;
    using System.Security;

    /// <summary>
    /// Extensions methods for SecureString.
    /// </summary>
    public static class SecureStringExtensions
    {
        /// <summary>
        /// Gets a plaintext string value from a SecureString to use in APIs that don't accept SecureString parameters.
        /// </summary>
        /// <param name="secureString"></param>
        /// <returns></returns>
        public static string ToUnsecureString(this SecureString secureString)
        {
            if (secureString == null)
            {
                throw new ArgumentNullException();
            }

            IntPtr unmanagedString = IntPtr.Zero;

            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                return Marshal.PtrToStringUni(unmanagedString);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }
    }
}