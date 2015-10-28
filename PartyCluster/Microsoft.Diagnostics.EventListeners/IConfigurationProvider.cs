// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;

namespace Microsoft.Diagnostics.EventListeners
{
    
    public interface IConfigurationProvider
    {
        string GetValue(string name);
        event EventHandler ConfigurationChanged;
    }
}
