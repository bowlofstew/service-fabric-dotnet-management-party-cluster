// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Domain
{
    using System.Net.Mail;
    using System.Threading.Tasks;

    public interface ISendMail
    {
        Task SendMessageAsync(MailAddress from, string to, string subject, string htmlBody);
    }
}