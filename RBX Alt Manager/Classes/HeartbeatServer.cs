using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace RBX_Alt_Manager.Classes
{
    public class HeartbeatServer
    {
        private HttpListener _listener;
        private bool _running;
        private readonly object _lock = new object();

        public static ConcurrentDictionary<string, DateTime> LastHeartbeat { get; private set; }

        public HeartbeatServer()
        {
            LastHeartbeat = new ConcurrentDictionary<string, DateTime>();
        }

        public void Start()
        {
            if (_running) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:12211/");
                _listener.Start();
                _running = true;

                ThreadPool.QueueUserWorkItem((o) =>
                {
                    while (_listener.IsListening)
                    {
                        try
                        {
                            ThreadPool.QueueUserWorkItem((c) =>
                            {
                                var ctx = c as HttpListenerContext;
                                try
                                {
                                    HandleRequest(ctx);
                                }
                                catch (Exception ex)
                                {
                                    Program.Logger.Error($"Heartbeat request error: {ex}");
                                }
                                finally
                                {
                                    ctx?.Response.OutputStream.Close();
                                }
                            }, _listener.GetContext());
                        }
                        catch (Exception ex)
                        {
                            if (_running) // Only log if not shutting down
                                Program.Logger.Error($"Heartbeat server listener error: {ex}");
                        }
                    }
                });

                Program.Logger.Info("Heartbeat server started on localhost:12211");
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"Failed to start heartbeat server: {ex}");
                throw;
            }
        }

        public void Stop()
        {
            if (!_running) return;

            _running = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (Exception ex)
            {
                Program.Logger.Error($"Error stopping heartbeat server: {ex}");
            }

            Program.Logger.Info("Heartbeat server stopped");
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            var request = ctx.Request;
            var response = ctx.Response;

            try
            {
                if (request.HttpMethod != "POST")
                {
                    response.StatusCode = 405;
                    response.ContentType = "text/plain";
                    var bytes = Encoding.UTF8.GetBytes("Method not allowed");
                    response.OutputStream.Write(bytes, 0, bytes.Length);
                    return;
                }

                string body;
                using (var reader = new StreamReader(request.InputStream))
                {
                    body = reader.ReadToEnd();
                }

                string username = ExtractUsernameFromBody(body);

                if (string.IsNullOrEmpty(username))
                {
                    response.StatusCode = 400;
                    response.ContentType = "text/plain";
                    var bytes = Encoding.UTF8.GetBytes("Missing 'name' parameter");
                    response.OutputStream.Write(bytes, 0, bytes.Length);
                    return;
                }

                LastHeartbeat.AddOrUpdate(username, DateTime.Now, (key, oldValue) => DateTime.Now);

                response.StatusCode = 200;
                response.ContentType = "text/plain";
                var responseBytes = Encoding.UTF8.GetBytes("OK");
                response.OutputStream.Write(responseBytes, 0, responseBytes.Length);

                Program.Logger.Debug($"Heartbeat received for user: {username}");
            }
            catch (Exception ex)
            {
                response.StatusCode = 500;
                response.ContentType = "text/plain";
                var errorBytes = Encoding.UTF8.GetBytes("Internal server error");
                response.OutputStream.Write(errorBytes, 0, errorBytes.Length);

                Program.Logger.Error($"Heartbeat request handling error: {ex}");
            }
        }

        private string ExtractUsernameFromBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;

            var pairs = body.Split('&');
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2 && keyValue[0].Trim() == "name")
                {
                    return Uri.UnescapeDataString(keyValue[1].Trim());
                }
            }

            return null;
        }
    }
}