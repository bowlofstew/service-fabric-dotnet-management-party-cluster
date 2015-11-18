// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    public class HyperlinkView
    {
        public HyperlinkView(string link, string text, string description)
        {
            this.Link = link;
            this.Text = text;
            this.Description = description;
        }

        public string Link { get; private set; }

        public string Text { get; private set; }

        public string Description { get; private set; }
    }
}
