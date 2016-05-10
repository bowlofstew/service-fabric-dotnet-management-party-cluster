// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.ViewModels
{
    internal struct BadRequestViewModel
    {
        public BadRequestViewModel(string code, string message, string error)
        {
            this.Code = code;
            this.Message = message;
            this.Error = error;
        }

        public string Error { get; private set; }

        public string Message { get; private set; }

        public string Code { get; private set; }
    }
}