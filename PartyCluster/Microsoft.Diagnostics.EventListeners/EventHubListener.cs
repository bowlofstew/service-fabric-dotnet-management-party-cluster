// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MessagingEventData = Microsoft.ServiceBus.Messaging.EventData;

namespace Microsoft.Diagnostics.EventListeners
{
    public class EventHubListener : BufferingEventListener
    {
        private const int ConcurrentConnections = 4;

        private class EventHubConnectionData
        {
            public string EventHubName;
            public MessagingFactory[] MessagingFactories;
        }

        private EventHubConnectionData connectionData;

        public EventHubListener(IConfigurationProvider configurationProvider): base(configurationProvider)
        {
            if (this.Disabled)
            {
                return;
            }

            Debug.Assert(configurationProvider != null);
            CreateConnectionData(configurationProvider);

            Sender = new ConcurrentEventSender<EventData>(
                eventBufferSize: 1000,
                maxConcurrency: ConcurrentConnections,
                batchSize: 50,
                noEventsDelay: TimeSpan.FromMilliseconds(1000),
                transmitterProc: SendEventsAsync);
        }

        private void CreateConnectionData(object sender)
        {
            var configurationProvider = (IConfigurationProvider)sender;

            string serviceBusConnectionString = configurationProvider.GetValue("serviceBusConnectionString");
            if (string.IsNullOrWhiteSpace(serviceBusConnectionString))
            {
                throw new ConfigurationErrorsException("Configuraiton parameter 'serviceBusConnectionString' must be set to a valid Service Bus connection string");
            }

            string eventHubName = configurationProvider.GetValue("eventHubName");
            if (string.IsNullOrWhiteSpace(eventHubName))
            {
                throw new ConfigurationErrorsException("Configuration parameter 'eventHubName' must not be empty");
            }

            this.connectionData = new EventHubConnectionData();
            this.connectionData.EventHubName = eventHubName;

            var connStringBuilder = new ServiceBusConnectionStringBuilder(serviceBusConnectionString);
            connStringBuilder.TransportType = TransportType.Amqp;
            this.connectionData.MessagingFactories = new MessagingFactory[ConcurrentConnections];
            for (uint i = 0; i < ConcurrentConnections; i++)
            {
                this.connectionData.MessagingFactories[i] = MessagingFactory.CreateFromConnectionString(connStringBuilder.ToString());
            }
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

                var factory = this.connectionData.MessagingFactories[transmissionSequenceNumber % ConcurrentConnections];
                EventHubClient hubClient;
                lock(factory)
                {
                    hubClient = factory.CreateEventHubClient(this.connectionData.EventHubName);
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
