// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Common
{
    using System.Diagnostics.Tracing;
    using System.Threading.Tasks;

    [EventSource(Name = "ServiceFabric-PartyCluster-ClusterService")]
    public sealed class TraceEventSource : EventSource
    {
        public static readonly TraceEventSource Current = new TraceEventSource();

        private const int SchemaVersion = 1;

        // These event ids must be unique and 0 < eventId < 2^16
        private const int MessageEventId = 1;
        private const int ErrorEventId = 2;

        static TraceEventSource()
        {
            // A workaround for the problem where ETW activities do not get tracked until Tasks infrastructure is initialized.
            // This problem will be fixed in .NET Framework 4.6.2.
            Task.Run(() => { });
        }

        // Instance constructor is private to enforce singleton semantics
        private TraceEventSource() : base()
        {
        }

        [NonEvent]
        public void Message(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                this.Message(finalMessage);
            }
        }

        [Event(MessageEventId, Level = EventLevel.Informational,
            Keywords = Keywords.PartyClusters | Keywords.Trace, Version = SchemaVersion)]
        public void Message(string message)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(MessageEventId, message);
            }
        }

        [NonEvent]
        public void Error(string message, params object[] args)
        {
            if (this.IsEnabled())
            {
                string finalMessage = string.Format(message, args);
                this.Error(finalMessage);
            }
        }

        [Event(ErrorEventId, Level = EventLevel.Error,
            Keywords = Keywords.PartyClusters | Keywords.Trace, Version = SchemaVersion)]
        public void Error(string message)
        {
            if (this.IsEnabled())
            {
                this.WriteEvent(ErrorEventId, message);
            }
        }

        public static class Keywords
        {
            public const EventKeywords PartyClusters = (EventKeywords)2;
            public const EventKeywords Trace = (EventKeywords)4;
        }
    }
}