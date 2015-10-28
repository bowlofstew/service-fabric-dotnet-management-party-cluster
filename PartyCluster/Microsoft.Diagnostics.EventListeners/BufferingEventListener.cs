// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Microsoft.Diagnostics.Tracing;

namespace Microsoft.Diagnostics.EventListeners
{
    public abstract class BufferingEventListener: EventListener
    {
        protected ConcurrentEventSender<EventData> Sender { get; set; }

        public bool ApproachingBufferCapacity
        {
            get { return Sender.ApproachingBufferCapacity; }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventArgs)
        {
            Sender.SubmitEvent(eventArgs.ToEventData());
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            EnableEvents(eventSource, EventLevel.LogAlways, (EventKeywords)~0);
        }
    }
}
