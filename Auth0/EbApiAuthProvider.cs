using ExpressBase.Common.Constants;
using ExpressBase.Common.ServiceStack.Auth;
using ExpressBase.Security;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Configuration;
using ServiceStack.Redis;
using ServiceStack.Web;
using System;
using System.Collections.Generic;
using System.Text;

namespace ExpressBase.ServiceStack.Auth0
{
    public class EbApiAuthProvider : ApiKeyAuthProvider
    {
        public EbApiAuthProvider(IAppSettings appSettings) : base(appSettings)
        {
        }

        public void PopulateSession(IUserAuthRepository authRepo, IUserAuth userAuth, CustomUserSession session, string userId)
        {
            if (authRepo == null)
                return;

            var holdSessionId = session.Id;
            session.PopulateWith(userAuth); //overwrites session.Id
            session.Id = holdSessionId;
            session.IsAuthenticated = true;
            session.UserAuthId = userId;

            string temp = userId.Substring(userId.IndexOf(CharConstants.COLON) + 1);
            session.Email = temp.Substring(0, temp.IndexOf(CharConstants.COLON));
            session.Uid = (userAuth as User).UserId;
            session.WhichConsole = userId.Substring(userId.Length - 2);
            session.Roles.Clear();
            session.Permissions.Clear();
        }
    }
}
