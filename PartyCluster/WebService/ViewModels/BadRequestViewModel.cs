using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Domain;

namespace WebService.ViewModels
{
    internal struct BadRequestViewModel
    {
        public BadRequestViewModel(string code, string message)
        {
            this.Code = code;
            this.Message = message;
        }

        public string Message { get; private set; }

        public string Code { get; private set; }
    }
}
