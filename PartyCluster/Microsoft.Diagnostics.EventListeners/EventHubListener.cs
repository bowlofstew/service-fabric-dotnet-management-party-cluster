// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MessagingEventData = Microsoft.ServiceBus.Messaging.EventData;

namespace Microsoft.Diagnostics.EventListeners
{
    public class EventHubListener : BufferingEventListener
    {
        private string contextInfo;
        private uint concurrentConnections;
        private MessagingFactory[] messagingFactories;
        private string eventHubName;

        public EventHubListener(string contextInfo, string serviceBusConnectionString, string eventHubName, uint concurrentConnections = 2)
        {
            if (string.IsNullOrWhiteSpace(serviceBusConnectionString))
            {
                throw new ArgumentException("Must pass a nonh-empty Service Bus connection string", "serviceBusConnectionString");
            }
            if (string.IsNullOrWhiteSpace("eventHubName"))
            {
                throw new ArgumentException("Event Hub name must not be empty", "eventHubName");
            }
            if (concurrentConnections == 0)
            {
                throw new ArgumentOutOfRangeException("concurrentConnections", "Number of concurrent connections cannot be zero");
            }

            this.concurrentConnections = concurrentConnections;
            this.eventHubName = eventHubName;

            var connStringBuilder = new ServiceBusConnectionStringBuilder(serviceBusConnectionString);
            connStringBuilder.TransportType = TransportType.Amqp;
            this.messagingFactories = new MessagingFactory[this.concurrentConnections];
            for(uint i=0; i < this.concurrentConnections; i++)
            {
                this.messagingFactories[i] = MessagingFactory.CreateFromConnectionString(connStringBuilder.ToString());
            }

            Sender = new ConcurrentEventSender<EventData>(
                contextInfo: contextInfo,
                eventBufferSize: 1000,
                maxConcurrency: this.concurrentConnections,
                batchSize: 50,
                noEventsDelay: TimeSpan.FromMilliseconds(1000),
                transmitterProc: SendEventsAsync);

            this.contextInfo = contextInfo;
        }

        

        private async Task SendEventsAsync(IEnumerable<EventData> events, long transmissionSequenceNumber, CancellationToken cancellationToken)
        {
            if (events == null)
            {
                return;
            }

            try
            {
                var batch = new List<MessagingEventData>();

                foreach(EventData eventData in events)
                {
                    var messagingEventData = eventData.ToMessagingEventData();
                    batch.Add(messagingEventData);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    // TODO: report that event sending has been cancelled 
                    return;
                }

                var factory = this.messagingFactories[transmissionSequenceNumber % this.concurrentConnections];
                EventHubClient hubClient;
                lock(factory)
                {
                    hubClient = factory.CreateEventHubClient(this.eventHubName);
                }

                await hubClient.SendBatchAsync(batch);
            }
            catch (Exception)
            {
                // TODO report EventHub upload error (e.ToString())
            }
        }        
    }
}
