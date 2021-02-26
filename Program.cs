using System;
using System.Threading.Tasks;
using ExpressBase.Common;
using ExpressBase.Common.Constants;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
namespace ExpressBase.AuthServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            string env = Environment.GetEnvironmentVariable(EnvironmentConstants.ASPNETCORE_ENVIRONMENT);
            //if (env == CoreConstants.PRODUCTION)
            //    Task.Run(() => EbWebsocket.InitialiseWssConnection());

            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>().UseKestrel(options =>
                {
                    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(7);
                    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
                })
                .UseUrls(urls: "http://*:41000/");
    }
}
