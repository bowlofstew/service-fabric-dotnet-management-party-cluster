// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.Domain
{
    using System.Runtime.Serialization;

    [DataContract]
    public class ApplicationView
    {
        public ApplicationView(HyperlinkView link)
        {
            this.EntryServiceInfo = link;
        }

        [DataMember]
        public HyperlinkView EntryServiceInfo { get; private set; }
    }
}