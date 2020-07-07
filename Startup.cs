using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExpressBase.Common;
using ExpressBase.Common.Constants;
using ExpressBase.Common.Data;
using ExpressBase.Common.ServiceStack.Auth;
using ExpressBase.ServiceStack.Auth0;
using Funq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ServiceStack;
using ServiceStack.Auth;
using ServiceStack.Logging;
using ServiceStack.Redis;

namespace ExpressBase.AuthServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseServiceStack(new AppHost());

        }

        public class AppHost : AppHostBase
        {
            public AppHost() : base("EXPRESSbase Auth", typeof(AppHost).Assembly) { }

            public override void Configure(Container container)
            {
                LogManager.LogFactory = new ConsoleLogFactory(debugEnabled: true);

                MyJwtAuthProvider jwtprovider = new MyJwtAuthProvider
                {
                    HashAlgorithm = "RS256",
                    PrivateKeyXml = Environment.GetEnvironmentVariable(EnvironmentConstants.EB_JWT_PRIVATE_KEY_XML),
                    PublicKeyXml = Environment.GetEnvironmentVariable(EnvironmentConstants.EB_JWT_PUBLIC_KEY_XML),
//#if (DEBUG)
                    RequireSecureConnection = false,
                    //EncryptPayload = true,
//#endif
                    ExpireTokensIn = TimeSpan.FromSeconds(90),
                    ExpireRefreshTokensIn = TimeSpan.FromHours(24),
                    PersistSession = true,
                    SessionExpiry = TimeSpan.FromHours(12),

                    CreatePayloadFilter = (payload, session) =>
                    {
                        payload[TokenConstants.SUB] = (session as CustomUserSession).UserAuthId;
                        payload[TokenConstants.CID] = (session as CustomUserSession).CId;
                        payload[TokenConstants.UID] = (session as CustomUserSession).Uid.ToString();
                        payload[TokenConstants.WC] = (session as CustomUserSession).WhichConsole;
                        payload[TokenConstants.IP] = (session as CustomUserSession).SourceIp;
                    },

                    PopulateSessionFilter = (session, token, req) =>
                    {
                        var csession = session as CustomUserSession;
                        csession.UserAuthId = token[TokenConstants.SUB];
                        csession.CId = token[TokenConstants.CID];
                        csession.Uid = Convert.ToInt32(token[TokenConstants.UID]);
                        csession.WhichConsole = token[TokenConstants.WC];
                        csession.SourceIp = token[TokenConstants.IP];
                    }
                };

                this.Plugins.Add(new AuthFeature(() =>
                                new CustomUserSession(),
                                new IAuthProvider[]
                                {
                                    new MyCredentialsAuthProvider(AppSettings) { PersistSession = true },
                                    jwtprovider,
                                }));

                string env = Environment.GetEnvironmentVariable(EnvironmentConstants.ASPNETCORE_ENVIRONMENT);


                var redisServer = Environment.GetEnvironmentVariable(EnvironmentConstants.EB_REDIS_SERVER);

                //if (env == "Staging")
                //{
                //    container.Register<IRedisClientsManager>(c => new RedisManagerPool(redisServer));
                //}
                //else
                //{
                    var redisPassword = Environment.GetEnvironmentVariable(EnvironmentConstants.EB_REDIS_PASSWORD);
                    var redisPort = Environment.GetEnvironmentVariable(EnvironmentConstants.EB_REDIS_PORT);
                    var redisConnectionString = string.Format("redis://{0}@{1}:{2}", redisPassword, redisServer, redisPort);
                    container.Register<IRedisClientsManager>(c => new RedisManagerPool(redisConnectionString));
                //}

                container.Register<IAuthRepository>(c => new MyRedisAuthRepository(c.Resolve<IRedisClientsManager>()));

                container.Register(c => c.Resolve<IRedisClientsManager>().GetCacheClient());
                container.Register<JwtAuthProvider>(jwtprovider);
                container.Register<IEbConnectionFactory>(c => new EbConnectionFactory(c)).ReusedWithin(ReuseScope.Request);

                this.GlobalRequestFilters.Add((req, res, requestDto) =>
                {
                    ILog log = LogManager.GetLogger(GetType());

                    try
                    {
                        if (requestDto.GetType() == typeof(Authenticate))
                        {
                            log.Info("In Authenticate");

                            string TenantId = (requestDto as Authenticate).Meta != null ? (requestDto as Authenticate).Meta[TokenConstants.CID] : CoreConstants.EXPRESSBASE;
                            log.Info(TenantId);
                            RequestContext.Instance.Items.Add(CoreConstants.SOLUTION_ID, TenantId);
                        }
                    }
                    catch (Exception e)
                    {
                        log.Info("ErrorStackTrace..........." + e.StackTrace);
                        log.Info("ErrorMessage..........." + e.Message);
                        log.Info("InnerException..........." + e.InnerException);
                    }
                });

            }
        }
    }
}
