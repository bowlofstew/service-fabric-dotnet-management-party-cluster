// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System.Runtime.Serialization;

namespace Domain
{
    [DataContract]
    public class HyperlinkView
    {
        public HyperlinkView(string link, string text, string description)
        {
            this.Address = link;
            this.Text = text;
            this.Description = description;
        }

        [DataMember]
        public string Address { get; private set; }

        [DataMember]
        public string Text { get; private set; }

        [DataMember]
        public string Description { get; private set; }
    }
}
