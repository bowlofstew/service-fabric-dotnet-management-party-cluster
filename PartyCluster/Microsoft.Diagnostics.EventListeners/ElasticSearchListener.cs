// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------
using Nest;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.EventListeners
{
    public class ElasticSearchListener : BufferingEventListener, IDisposable
    {
        private const string Dot = ".";
        private const string Dash = "-";

        // TODO: make it a (configuration) property of the listener
        private const string EventDocumentTypeName = "event";

        private ElasticClient esClient;
        private string indexNamePrefix;
        private string lastIndexName;
        private string contextInfo;

        // TODO: support for multiple ES nodes/connection pools, for failover and load-balancing
        public ElasticSearchListener(string contextInfo, Uri serverUri, string userName, string password, string indexNamePrefix)
        {
            if (serverUri == null || !serverUri.IsAbsoluteUri)
            {
                throw new ArgumentException("serverUri must be a valid, absolute URI", "serverUri");
            }

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("Invalid Elastic Search credentials");
            }

            Sender = new ConcurrentEventSender<EventData>(
                contextInfo: contextInfo,
                eventBufferSize: 1000,
                maxConcurrency: 2,
                batchSize: 100,
                noEventsDelay: TimeSpan.FromMilliseconds(1000),
                transmitterProc: SendEventsAsync);

            this.contextInfo = contextInfo;

            this.indexNamePrefix = string.IsNullOrWhiteSpace(indexNamePrefix) ? string.Empty : indexNamePrefix + Dash;
            this.lastIndexName = null;

            var config = new ConnectionSettings(serverUri).SetBasicAuthentication(userName, password);
            this.esClient = new ElasticClient(config);
        }

        private async Task SendEventsAsync(IEnumerable<EventData> events, long transmissionSequenceNumber, CancellationToken cancellationToken)
        {
            if (events == null)
            {
                return;
            }

            try
            {
                string currentIndexName = GetIndexName();
                if (!string.Equals(currentIndexName, this.lastIndexName, StringComparison.Ordinal))
                {
                    await EnsureIndexExists(currentIndexName);
                    this.lastIndexName = currentIndexName;
                }

                var request = new BulkRequest();
                request.Refresh = true;

                var operations = new List<IBulkOperation>();
                foreach (EventData eventData in events)
                {
                    var operation = new BulkCreateOperation<EventData>(eventData);
                    operation.Index = currentIndexName;
                    operation.Type = EventDocumentTypeName;
                    operations.Add(operation);
                }

                request.Operations = operations;

                if (cancellationToken.IsCancellationRequested)
                {
                    // TODO: report that event sending has been cancelled               
                    return;
                }

                // Note: the NEST client is documented to be thread-safe so it should be OK to just reuse the this.esClient instance
                // between different SendEventsAsync callbacks.
                // Reference: https://www.elastic.co/blog/nest-and-elasticsearch-net-1-3
                IBulkResponse response = await this.esClient.BulkAsync(request);
                if (!response.IsValid)
                {
                    ReportEsRequestError(response, "Bulk upload");
                }
            }
            catch (Exception)
            {
                // TODO report ES upload error (e.ToString())
            }
        }

        private async Task EnsureIndexExists(string currentIndexName)
        {
            var existsResult = await this.esClient.IndexExistsAsync(currentIndexName);
            if (!existsResult.IsValid)
            {
                ReportEsRequestError(existsResult, "Index exists check");
            }

            if (existsResult.Exists)
            {
                return;
            }

            // TODO: allow the consumer to fine-tune index settings
            var indexSettings = new IndexSettings();
            indexSettings.NumberOfReplicas = 1;
            indexSettings.NumberOfShards = 5;
            indexSettings.Settings.Add("refresh_interval", "15s");

            var createIndexResult = await this.esClient.CreateIndexAsync(c => c.Index(currentIndexName).InitializeUsing(indexSettings));

            if (!createIndexResult.IsValid)
            {
                if (createIndexResult.ServerError != null && string.Equals(createIndexResult.ServerError.ExceptionType, "IndexAlreadyExistsException", StringComparison.OrdinalIgnoreCase))
                {
                    // This is fine, someone just beat us to create a new index.
                    return;
                }

                ReportEsRequestError(createIndexResult, "Create index");
            }
        }

        private string GetIndexName()
        {
            var now = DateTimeOffset.UtcNow;
            var retval = this.indexNamePrefix + now.Year.ToString() + Dot + now.Month.ToString() + Dot + now.Day.ToString();
            return retval;
        }

        private void ReportEsRequestError(IResponse response, string request)
        {
            Debug.Assert(!response.IsValid);

            if (response.ServerError != null)
            {
                // TODO: report response.ServerError.Error, response.ServerError.ExceptionType, response.ServerError.Status
            }
            else
            {
                // TODO: report unknown ES communication error                
            }
        }
    }
}
