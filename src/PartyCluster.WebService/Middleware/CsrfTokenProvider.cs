// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace PartyCluster.WebService.Middleware
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using Common;

    public class CsrfTokenProvider
    {
        private string csrfSalt = string.Empty;

        public CsrfTokenProvider(ConfigSettings config)
        {
#if LOCAL
            this.csrfSalt = "qwerty6543";
#else
            this.csrfSalt = config.CsrfSalt.ToUnsecureString();
#endif
        }

        public string GenerateCsrfToken(string authToken)
        {
            return this.GenerateHash(authToken);
        }

        public bool IsCsrfTokenValid(string csrfToken, string authToken)
        {
            return csrfToken == this.GenerateHash(authToken);
        }

        private string GenerateHash(string authToken)
        {
            using (var sha256 = SHA256.Create())
            {
                var computedHash = sha256.ComputeHash(
                    Encoding.Unicode.GetBytes(
                        authToken + this.csrfSalt));

                var encodedHash = UrlTokenEncode(computedHash);
                return encodedHash;
            }
        }

        // Borrowed from Mono System.Web HttpServerUtility.UrlTokenEncode
        private static string UrlTokenEncode(byte[] input)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            if (input.Length < 1)
            {
                return string.Empty;
            }

            string base64 = Convert.ToBase64String(input);
            int retlen;
            if (base64 == null || (retlen = base64.Length) == 0)
            {
                return string.Empty;
            }

            // MS.NET implementation seems to process the base64
            // string before returning. It replaces the chars:
            //
            //  + with -
            //  / with _
            //
            // Then removes trailing ==, which may appear in the
            // base64 string, and replaces them with a single digit
            // that's the count of removed '=' characters (0 if none
            // were removed)
            int equalsCount = 0x30;
            while (retlen > 0 && base64[retlen - 1] == '=')
            {
                equalsCount++;
                retlen--;
            }

            char[] chars = new char[retlen + 1];
            chars[retlen] = (char)equalsCount;
            for (int i = 0; i < retlen; i++)
            {
                switch (base64[i])
                {
                    case '+':
                        chars[i] = '-';
                        break;

                    case '/':
                        chars[i] = '_';
                        break;

                    default:
                        chars[i] = base64[i];
                        break;
                }
            }
            return new string(chars);
        }
    }
}
