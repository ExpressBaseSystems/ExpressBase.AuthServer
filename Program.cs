using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExpressBase.AuthServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseStartup<Startup>().UseKestrel(options => {
                    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(7);
                    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
                })
                .UseUrls(urls: "http://*:41000/");
    }
}
