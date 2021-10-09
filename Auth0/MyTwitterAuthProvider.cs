using System.Collections.Generic;
using ExpressBase.Data;
using ExpressBase.Objects.ServiceStack_Artifacts;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using ServiceStack.Web;
using ExpressBase.Common;
using ExpressBase.Common.Data;
using ExpressBase.Common.Constants;
using ExpressBase.Common.Structures;
using ExpressBase.Common.ServiceStack.Auth;
using Newtonsoft.Json;
using System;
using System.Data.Common;
using System.Threading.Tasks;
using ExpressBase.Common.Extensions;

namespace ExpressBase.ServiceStack
{
    public class MyTwitterAuthProvider : TwitterAuthProvider
    {

        public MyTwitterAuthProvider(IAppSettings settings) : base(settings) { }
    }
}