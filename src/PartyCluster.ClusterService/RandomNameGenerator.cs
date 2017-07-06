// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.ClusterService
{
    using System;

    public class RandomNameGenerator
    {
        private static readonly Random random = new Random();
        private static readonly object nameSyncLock = new object();
        private static readonly object idSyncLock = new object();

        public static string GetRandomNameString(string prefix)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var stringChars = new char[8];

            for (int index = 0; index < stringChars.Length; index++)
            {
                var next = 0;
                lock(nameSyncLock)
                {
                    next = random.Next(chars.Length);
                }

                stringChars[index] = chars[next];
            }

            var nameString = new String(stringChars);
            return string.Format("{0}{1}", prefix, nameString);
        }

        public static int GetRandomId()
        {
            var next = 0;
            lock (idSyncLock)
            {
                next = random.Next();
            }

            return next;
        }
    }
}
