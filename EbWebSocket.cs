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
                Console.WriteLine("WS STARTING");
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
                Console.WriteLine(alertJson);
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
                        string Query = string.Format("INSERT INTO delivery_status(status, transaction_id, message, eb_created_at) VALUES ('{0}','{1}', '{2}', Now());", status, txn_id, message);
                        EbConnectionFactory.DataDB.DoQuery(Query);
                        string Query2 = string.Format(@"UPDATE delivery_requests SET currentstatus = '{0}' WHERE auto_id = '{1}';", status, txn_id);
                        EbConnectionFactory.DataDB.DoQuery(Query2);
                    }
                    else if (alerttype == AlertTypes.BROADCAST_REQUEST.ToString())
                    {
                        string[] txn_ids = (alert.ContainsKey("txIDs")) ? alert.GetValue("txIDs").ToObject<string[]>() : null;
                        string status = (alert.ContainsKey("status")) ? alert.GetValue("status").ToString() : string.Empty;
                        string message = (alert.ContainsKey("message")) ? alert.GetValue("message").ToString() : string.Empty;
                        for (int i = 0; i < txn_ids.Length; i++)
                        {
                            EbConnectionFactory EbConnectionFactory = new EbConnectionFactory(EbWebsocket.DbName, RedisClient);
                            string Query = string.Format("INSERT INTO delivery_status(status, transaction_id, message, eb_created_at) VALUES ('{0}','{1}', '{2}', Now());", status, txn_ids[i], message);
                            EbConnectionFactory.DataDB.DoQuery(Query);
                        }
                    }
                    else if (alerttype == AlertTypes.TRIP_STARTED.ToString())
                    {
                        JObject details = JObject.Parse(alert.GetValue("tripDetails").ToString());
                        int ch_tripid = (details.ContainsKey("tripId")) ? Convert.ToInt32(details.GetValue("tripId")) : 0;
                        int ch_driverid = (details.ContainsKey("driverId")) ? Convert.ToInt32(details.GetValue("driverId")) : 0;
                        string driver_name = (alert.ContainsKey("user")) ? alert.GetValue("user").ToString() : string.Empty;
                        string status = (details.ContainsKey("tripStatus")) ? details.GetValue("tripStatus").ToString() : string.Empty;

                        EbConnectionFactory EbConnectionFactory = new EbConnectionFactory(EbWebsocket.DbName, RedisClient);
                        string Query1 = string.Format(@"INSERT INTO trips(ch_trip_id, ch_driver_id, driver_name, eb_created_at)  
                                                    VALUES ({0}, {1}, '{2}', Now()) RETURNING id;", ch_tripid, ch_driverid, driver_name);
                        int eb_trips_id = EbConnectionFactory.DataDB.ExecuteScalar<int>(Query1);

                        string Query2 = string.Format(@"INSERT INTO trip_status(trips_id, ch_trip_id, status, eb_created_at)  
                                                    VALUES ({0}, {1}, '{2}', Now()) RETURNING id;", eb_trips_id, ch_tripid, status);
                        EbConnectionFactory.DataDB.ExecuteScalar<int>(Query2);

                    }
                    else if (alerttype == AlertTypes.TRIP_ENDED.ToString())
                    {
                        if (alert.ContainsKey("tripDetails"))
                        {
                            double distanceCovered;
                            JObject details = JObject.Parse(alert.GetValue("tripDetails").ToString());

                            int ch_tripid = (details.ContainsKey("tripId")) ? Convert.ToInt32(details.GetValue("tripId")) : 0;
                            string status = (details.ContainsKey("tripStatus")) ? details.GetValue("tripStatus").ToString() : string.Empty;

                            string Query2 = string.Format(@"INSERT INTO trip_status(trips_id, ch_trip_id, status, eb_created_at)  
                                                    SELECT id, {0}, '{1}', Now() FROM trips WHERE ch_trip_id = {0};", ch_tripid, status);
                            EbConnectionFactory EbConnectionFactory = new EbConnectionFactory(EbWebsocket.DbName, RedisClient);
                            EbConnectionFactory.DataDB.ExecuteScalar<int>(Query2);
                            Uri uri = new Uri("https://chaalak.ai/Chaalak/rest/integration/trip/getTripById?tripId=" + ch_tripid);
                            try
                            {
                                RestClient client = new RestClient(uri.GetLeftPart(UriPartial.Authority));
                                RestRequest request = new RestRequest(uri.PathAndQuery, RestSharp.Method.GET);
                                request.AddHeader("Authorization", $"Bearer " + GetToken());
                                IRestResponse resp = client.Execute(request);

                                if (resp.IsSuccessful)
                                {
                                    object result = resp.Content;
                                    JObject trip_details = JObject.Parse(result.ToString());
                                    distanceCovered = (details.ContainsKey("distanceCovered")) ? Convert.ToDouble(details.GetValue("distanceCovered")) / 1000 : 0;

                                }
                                else
                                    throw new Exception($"Failed to execute api [{ "getTripById"}], {resp.ErrorMessage}, {resp.Content}");
                            }
                            catch (Exception ex)
                            {
                                throw new Exception("[ExecuteThirdPartyApi], " + ex.Message);
                            }
                            distanceCovered = 1.57;
                            double rate = 20 * distanceCovered;//hardcoded value
                            string Query = string.Format(@"UPDATE trips SET distance_covered = {0}, rate = {1}, eb_modified_at = NOW() WHERE ch_trip_id = {2};", distanceCovered, rate, ch_tripid);
                            int eb_trips_id = EbConnectionFactory.DataDB.ExecuteScalar<int>(Query);

                            //JArray broadcast_details = JArray.Parse(details.GetValue("tripDetails").ToString()); && facility id
                            //for (int i = 0; i < broadcast_details.Count; i++)
                            //{
                            //  int broadcast_id = Convert.ToInt32(broadcast_details[i]["broadcastRequestId"]);
                            //} 
                            Uri uri2 = new Uri("https://chaalak.ai/Chaalak/rest/integration/trip/updateTripAmount?tripAmount=" + rate + "&tripId=" + ch_tripid);
                            try
                            {
                                RestClient client = new RestClient(uri2.GetLeftPart(UriPartial.Authority));
                                RestRequest request = new RestRequest(uri2.PathAndQuery, RestSharp.Method.GET);
                                request.AddHeader("Authorization", $"Bearer " + GetToken());
                                IRestResponse resp = client.Execute(request);

                                if (resp.IsSuccessful)
                                {
                                    object result = resp.Content;
                                    JObject trip_details = JObject.Parse(result.ToString());
                                }
                                else
                                    throw new Exception($"Failed to execute api [{ "updateTripAmount"}], {resp.ErrorMessage}, {resp.Content}");
                            }
                            catch (Exception ex)
                            {
                                throw new Exception("[ExecuteThirdPartyApi], " + ex.Message);
                            }
                        }
                    }
                    else if (alerttype == AlertTypes.TRIP_EVENT_FIRED.ToString())
                        Console.WriteLine(alertJson);
                    else if (alerttype == AlertTypes.TRIP_STATUS_CHANGED.ToString())
                        Console.WriteLine(alertJson);
                    else
                    {
                        Console.WriteLine("----" + alertJson);
                    }
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
            BROADCAST_REQUEST,
            TRIP_ENDED,
            TRIP_STARTED
        }
    }
}
