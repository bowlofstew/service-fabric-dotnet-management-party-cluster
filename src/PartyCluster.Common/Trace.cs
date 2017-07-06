// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Common
{
    public class Trace
    {
        public static void Message(string message, params object[] args)
        {
            TraceEventSource.Current.Message(message, args);
        }

        public static void Error(string message, params object[] args)
        {
            TraceEventSource.Current.Error(message, args);
        }
    }
}
