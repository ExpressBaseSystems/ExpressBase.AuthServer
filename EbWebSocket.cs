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

        static EbConnectionFactory EbConnectionFactory = new EbConnectionFactory(EbWebsocket.DbName, RedisClient);

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
                        Handle_TX_STATUS_CHANGED(alert);
                    else if (alerttype == AlertTypes.BROADCAST_REQUEST.ToString())
                        Handle_BROADCAST_REQUEST(alert);
                    else if (alerttype == AlertTypes.TRIP_STARTED.ToString())//update driver in txn back 
                        Handle_TRIP_STARTED(alert);
                    else if (alerttype == AlertTypes.TRIP_ENDED.ToString())
                        Handle_TRIP_ENDED(alert);
                    else if (alerttype == AlertTypes.TRIP_EVENT_FIRED.ToString())
                        Console.WriteLine(alertJson);
                    else if (alerttype == AlertTypes.TRIP_STATUS_CHANGED.ToString())
                        Console.WriteLine(alertJson);
                    else
                        Console.WriteLine("----" + alertJson);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(alertJson + e.Message + e.StackTrace);
            }
        }

        static void Handle_BROADCAST_REQUEST(JObject alert)
        {
            string[] txn_ids = (alert.ContainsKey("txIDs")) ? alert.GetValue("txIDs").ToObject<string[]>() : null;
            string status = (alert.ContainsKey("status")) ? alert.GetValue("status").ToString() : string.Empty;
            string message = (alert.ContainsKey("message")) ? alert.GetValue("message").ToString() : string.Empty;
            int ch_broadcast_id = (alert.ContainsKey("requestId")) ? Convert.ToInt32(alert.GetValue("requestId")) : 0;
            int ch_facilityId = (alert.ContainsKey("facilityId")) ? Convert.ToInt32(alert.GetValue("facilityId")) : 0;
            JArray broadcast_details = JArray.Parse(alert.GetValue("broadcastTxDetails").ToString());
            System.Collections.Generic.List<string> customer_ids = new System.Collections.Generic.List<string>();
            foreach (JToken detail in broadcast_details)
            {
                string cid = detail["customerId"].ToString();
                if (!customer_ids.Contains(cid))
                    customer_ids.Add(cid);
            }
            string Query = string.Format(@"INSERT INTO broadcasts(status, transaction_ids, message, eb_created_at, ch_broadcast_id, ch_facility_id, customer_ids) 
                                               VALUES ('{0}','{1}', '{2}', Now(), {3}, {4},'{5}');", status, String.Join(",", txn_ids), message, ch_broadcast_id, ch_facilityId, String.Join(",", customer_ids));
            for (int i = 0; i < txn_ids.Length; i++)
            {
                Query += string.Format(@"INSERT INTO delivery_status(status, transaction_id, message, eb_created_at) 
                                               VALUES ('{0}','{1}', '{2}', Now());", status, txn_ids[i], message);
                if (status == "ACCEPTED")
                    Query += string.Format(@"UPDATE delivery_requests SET driver_accept = 'T' , currentstatus='DRIVER ACCEPTED' WHERE auto_id ='{0}';", txn_ids[i]);
                else
                    Query += string.Format(@"UPDATE delivery_requests SET driver_accept = 'F' , currentstatus='DRIVER REJECTED' WHERE auto_id ='{0}';", txn_ids[i]);
            }
           
            EbConnectionFactory.DataDB.DoQuery(Query);
        }

        static void Handle_TRIP_STARTED(JObject alert)
        {
            JObject details = JObject.Parse(alert.GetValue("tripDetails").ToString());
            int ch_tripid = (details.ContainsKey("tripId")) ? Convert.ToInt32(details.GetValue("tripId")) : 0;
            int ch_driverid = (details.ContainsKey("driverId")) ? Convert.ToInt32(details.GetValue("driverId")) : 0;
            string started_by = (alert.ContainsKey("user")) ? alert.GetValue("user").ToString() : string.Empty;
            string status = (details.ContainsKey("tripStatus")) ? details.GetValue("tripStatus").ToString() : string.Empty;
            JArray broadcast_details = JArray.Parse(details.GetValue("tripDetails").ToString());
            int ch_facility_id = Convert.ToInt32(broadcast_details?[0]["facilityId"]);
            System.Collections.Generic.List<int> broadcast_ids = new System.Collections.Generic.List<int>();
            for (int i = 0; i < broadcast_details.Count; i++)
                broadcast_ids.Add(Convert.ToInt32(broadcast_details[i]["id"]["broadcastRequestId"]));

            string Query1 = string.Format(@"UPDATE delivery_requests SET driver_id = AND currentstatus='ON PROGRESS' {0} WHERE auto_id = ANY(
            SELECT unnest((SELECT string_to_array(string_agg(transaction_ids,','),',') FROM broadcasts WHERE ch_broadcast_id IN({1}))));", ch_driverid, String.Join(",", broadcast_ids));

            Query1 += string.Format(@"INSERT INTO trips(ch_trip_id, ch_driver_id, started_by, eb_created_at, ch_facility_id, ch_broadcast_ids)  
                                            VALUES ({0}, {1}, '{2}', Now(), {3}, '{4}') RETURNING id;", ch_tripid, ch_driverid, started_by, ch_facility_id, String.Join(",", broadcast_ids));
            int eb_trips_id = EbConnectionFactory.DataDB.ExecuteScalar<int>(Query1);

            string Query2 = string.Format(@"INSERT INTO trip_status(trips_id, ch_trip_id, status, eb_created_at)  
                                            VALUES ({0}, {1}, '{2}', Now()) RETURNING id;", eb_trips_id, ch_tripid, status);
            EbConnectionFactory.DataDB.ExecuteScalar<int>(Query2);
        }

        static void Handle_TRIP_ENDED(JObject alert)
        {
            if (alert.ContainsKey("tripDetails"))
            {
                double distanceCovered;
                JObject details = JObject.Parse(alert.GetValue("tripDetails").ToString());

                int ch_tripid = (details.ContainsKey("tripId")) ? Convert.ToInt32(details.GetValue("tripId")) : 0;
                string status = (details.ContainsKey("tripStatus")) ? details.GetValue("tripStatus").ToString() : string.Empty;
                JArray broadcast_details = JArray.Parse(details.GetValue("tripDetails").ToString());
                int ch_facilityId = Convert.ToInt32(broadcast_details?[0]["facilityId"]);
                try
                {
                    Uri uri = new Uri("https://chaalak.ai/Chaalak/rest/integration/trip/getTripById?tripId=" + ch_tripid);
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

                string Query2 = string.Format(@"INSERT INTO trip_status(trips_id, ch_trip_id, status, eb_created_at)  
                                                SELECT id, {0}, '{1}', Now() FROM trips WHERE ch_trip_id = {0};", ch_tripid, status);
                EbConnectionFactory.DataDB.ExecuteScalar<int>(Query2);
                string Query = string.Format(@"UPDATE trips SET distance_covered = {0}, eb_modified_at = NOW() ,
                                                      rate = ({0} * (select rate_per_km from merchant_master where ch_facility_id = {2})) + 
                                                             ((select rate_per_customer from merchant_master where ch_facility_id = {2}))
                                               WHERE ch_trip_id = {1} returning rate;", distanceCovered, ch_tripid, ch_facilityId);
                double rate = EbConnectionFactory.DataDB.ExecuteScalar<Double>(Query);
                //No.of customers *15(a flat rate set by merchant) + (Distance for driver to reach facility + distance of trip till the last customer) *rate per km

                try
                {
                    Uri uri2 = new Uri("https://chaalak.ai/Chaalak/rest/integration/trip/updateTripAmount?tripAmount=" + rate + "&tripId=" + ch_tripid);
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

        static void Handle_TX_STATUS_CHANGED(JObject alert)
        {
            string txn_id = (alert.ContainsKey("txId")) ? alert.GetValue("txId").ToString() : string.Empty;
            string status = (alert.ContainsKey("status")) ? alert.GetValue("status").ToString() : string.Empty;
            string message = (alert.ContainsKey("message")) ? alert.GetValue("message").ToString() : string.Empty;

            string Query = string.Format(@"INSERT INTO delivery_status(status, transaction_id, message, eb_created_at) 
                                            VALUES ('{0}','{1}', '{2}', Now());", status, txn_id, message);
            Query += string.Format(@"UPDATE delivery_requests 
                                            SET currentstatus = '{0}' WHERE auto_id = '{1}';", status, txn_id);
            EbConnectionFactory.DataDB.DoQuery(Query);
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
