using ExpressBase.Common;
using ExpressBase.Common.Data;
using ExpressBase.Common.Extensions;
using ExpressBase.Common.Structures;
using ExpressBase.Data;
using ExpressBase.Objects.ServiceStack_Artifacts;
using Newtonsoft.Json;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using ServiceStack.Web;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

namespace ExpressBase.ServiceStack.Auth0
{
    internal class MyGithubAuthProvider : GithubAuthProvider
    {
        public MyGithubAuthProvider(IAppSettings settings) : base(settings) { }

    }
}