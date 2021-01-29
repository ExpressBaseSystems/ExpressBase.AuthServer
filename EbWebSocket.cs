using System; 
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Threading;
using System.Text;
using System.IO;
using Newtonsoft.Json.Linq;
using ExpressBase.Common;
using ServiceStack.Redis;
using ExpressBase.Common.Data;
using RestSharp;

namespace ExpressBase.AuthServer
{
    public class EbWebsocket
    {
        private static string DbName = "ebdb9q1tzdtek220200921075613";
        private static IRedisClient _redisClient;

        private static IRedisClient RedisClient
        {
            get
            {
                if (_redisClient == null)
                {
                    var redisServer = Environment.GetEnvironmentVariable(EnvironmentConstants.EB_REDIS_SERVER);
                    var redisPassword = Environment.GetEnvironmentVariable(EnvironmentConstants.EB_REDIS_PASSWORD);
                    var redisPort = Environment.GetEnvironmentVariable(EnvironmentConstants.EB_REDIS_PORT);
                    var redisConnectionString = string.Format("redis://{0}@{1}:{2}", redisPassword, redisServer, redisPort);
                    IRedisClient redisClient = new RedisManagerPool(redisConnectionString).GetClient();
                    _redisClient = redisClient;
                }
                return _redisClient;
            }
        }

        public static async Task InitialiseWssConnection()
        {
            do
            {
                Console.WriteLine("ENTERED");
                using (ClientWebSocket socket = new ClientWebSocket())
                    try
                    {
                        string token = GetToken();
                        Uri url = new Uri("wss://chaalak.ai/Chaalak/websocketendpointalert?token=" + token);
                        await socket.ConnectAsync(url, CancellationToken.None);
                        await Send(socket, "data");
                        await Receive(socket);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR - {ex.Message}");
                    }
            } while (true);
        }


        static string GetToken()
        {
            Uri uri = new Uri("https://chaalak.ai/Chaalak/rest/auth/login");
            string token;
            try
            {
                RestClient client = new RestClient(uri.GetLeftPart(UriPartial.Authority));
                RestRequest request = new RestRequest(uri.PathAndQuery, RestSharp.Method.POST);
                request.AddHeader("Content-type", "application/json");
                string s = "{\"username\":\"yunus@ewiresofttech.com\",\"password\":\"test\"}";
                request.AddJsonBody(s);
                IRestResponse resp = client.Execute(request);

                if (resp.IsSuccessful)
                {
                    object result = resp.Content;
                    JObject details = JObject.Parse(result.ToString());
                    token = details.ContainsKey("token") ? details["token"].ToString() : string.Empty;
                    if (token == string.Empty)
                        throw new Exception($"Failed to execute api [{ "https://chaalak.ai/Chaalak/rest/auth/login"}] Token is Null, {resp.ErrorMessage}, {resp.Content}");
                }
                else
                    throw new Exception($"Failed to execute api [{ "https://chaalak.ai/Chaalak/rest/auth/login"}], {resp.ErrorMessage}, {resp.Content}");
            }
            catch (Exception ex)
            {
                throw new Exception("[ExecuteThirdPartyApi], " + ex.Message);
            }
            return token;
        }

        static async Task Send(ClientWebSocket socket, string data) =>
                await socket.SendAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text, true, CancellationToken.None);

        static async Task Receive(ClientWebSocket socket)
        {
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[2048]);
            do
            {
                WebSocketReceiveResult result;
                using (MemoryStream ms = new MemoryStream())
                {
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    ms.Seek(0, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(ms, Encoding.UTF8))
                        HandleAlert((await reader.ReadToEndAsync()).ToString());
                }
            } while (true);
        }

        static void HandleAlert(string alertJson)
        {
            try
            {
                JObject alert = JObject.Parse(alertJson);
                if (alert.ContainsKey("alertType"))
                {
                    string alerttype = alert.GetValue("alertType").ToString();

                    if (alerttype == AlertTypes.TX_STATUS_CHANGED.ToString())
                    {
                        string txn_id = (alert.ContainsKey("txId")) ? alert.GetValue("txId").ToString() : string.Empty;
                        string status = (alert.ContainsKey("status")) ? alert.GetValue("status").ToString() : string.Empty;
                        string message = (alert.ContainsKey("message")) ? alert.GetValue("message").ToString() : string.Empty;
                        EbConnectionFactory EbConnectionFactory = new EbConnectionFactory(EbWebsocket.DbName, RedisClient);
                        string Query = string.Format("INSERT INTO delivery_status (status,transaction_id, message eb_created_at) VALUES ('{0}','{1}', '{2}', Now());", status, txn_id, message);
                        EbConnectionFactory.DataDB.DoQuery(Query);
                    }
                    else if (alerttype == AlertTypes.BROADCAST_REQUEST.ToString())
                    {
                        string[] txn_ids = (alert.ContainsKey("txIDs")) ? alert.GetValue("txIDs").ToObject<string[]>() : null;
                        string status = (alert.ContainsKey("status")) ? alert.GetValue("status").ToString() : string.Empty;
                        string message = (alert.ContainsKey("message")) ? alert.GetValue("message").ToString() : string.Empty;
                        for (int i = 0; i < txn_ids.Length; i++)
                        {
                            EbConnectionFactory EbConnectionFactory = new EbConnectionFactory(EbWebsocket.DbName, RedisClient);
                            string Query = string.Format("INSERT INTO delivery_status (status,transaction_id, eb_created_at) VALUES ('{0}','{1}', '{2}', Now());", status, txn_ids[i], message);
                            EbConnectionFactory.DataDB.DoQuery(Query);
                        }
                    }
                    else if (alerttype == AlertTypes.TRIP_EVENT_FIRED.ToString())
                    {

                        EbConnectionFactory EbConnectionFactory = new EbConnectionFactory(EbWebsocket.DbName, RedisClient);

                    }
                    // else if (alerttype == AlertTypes.TRIP_STATUS_CHANGED.ToString()) { }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(alertJson + e.Message + e.StackTrace);
            }
        }

        public enum AlertTypes
        {
            TX_STATUS_CHANGED,
            TRIP_STATUS_CHANGED,
            TRIP_EVENT_FIRED,
            BROADCAST_REQUEST
        }
    }
}
