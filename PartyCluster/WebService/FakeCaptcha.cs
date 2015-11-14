using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Domain;

namespace WebService
{
    internal class FakeCaptcha : ICaptcha
    {
        public Task<bool> VerifyAsync(string captchaResponse)
        {
            return Task.FromResult(true);
        }
    }
}
