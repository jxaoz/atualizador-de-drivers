using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using JsonObject = System.Collections.Generic.Dictionary<string, object>;

namespace DriverUpdater
{
    internal static class NvidiaAppClient
    {
        private const string DriverStatusTool = "nvapp_client_get_driver_status";
        private const string SessionHeader = "Mcp-Session-Id";
        private const int ResponseLimit = 256 * 1024;
        private static readonly Regex DriverVersion = new Regex(@"\A[0-9]{3,4}\.[0-9]{2}\z", RegexOptions.CultureInvariant);

        public static async Task<string> TryGetRecommendedVersionAsync(bool studio, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Uri endpoint = DiscoverEndpoint();
                return endpoint == null ? null : await QueryAsync(endpoint, studio, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }
            catch (IOException) { }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }

            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        private static Uri DiscoverEndpoint()
        {
            // NVIDIA writes this file for the running, enabled MCP server. A port alone is not discovery.
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(directory))
                return null;

            string path = Path.Combine(directory, "NVIDIA Corporation", "NvAppMcpServer", "server.json");
            using (FileStream file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(file, Encoding.UTF8))
            {
                var data = new char[4097];
                int length = reader.ReadBlock(data, 0, data.Length);
                if (length == data.Length)
                    return null;

                Uri endpoint;
                return Uri.TryCreate(Field(Parse(new string(data, 0, length)), "http") as string,
                    UriKind.Absolute, out endpoint) && IsNvidiaLoopbackEndpoint(endpoint) ? endpoint : null;
            }
        }

        private static bool IsNvidiaLoopbackEndpoint(Uri endpoint)
        {
            return endpoint.Scheme == Uri.UriSchemeHttp && endpoint.Host == "127.0.0.1" &&
                endpoint.UserInfo.Length == 0 && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0 &&
                (endpoint.AbsolutePath == "/mcp" || endpoint.AbsolutePath == "/mcp/") &&
                ((endpoint.Port >= 13508 && endpoint.Port <= 13518) ||
                 (endpoint.Port >= 49152 && endpoint.Port <= 49161));
        }

        private static async Task<string> QueryAsync(Uri endpoint, bool studio, CancellationToken cancellationToken)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var connection = new DriverStatusConnection(endpoint))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(7));
                CancellationToken token = deadline.Token;
                try
                {
                    if (!await connection.InitializeAsync(token).ConfigureAwait(false))
                        return null;

                    if (!await connection.AdvertisesToolAsync(token).ConfigureAwait(false))
                        return null;

                    JsonObject result = await connection.CallStatusAsync(token).ConfigureAwait(false);
                    return RecommendedVersion(result, studio);
                }
                finally
                {
                    connection.CloseSessionAsync().GetAwaiter().GetResult();
                }
            }
        }

        private static string RecommendedVersion(JsonObject result, bool studio)
        {
            if (result == null || Equals(Field(result, "isError"), true))
                return null;

            var status = Field(result, "structuredContent") as JsonObject;
            if (status == null)
            {
                // On legacy clients the same JSON may be carried by a single MCP text block.
                var content = Field(result, "content") as object[];
                var entry = content != null && content.Length == 1 ? content[0] as JsonObject : null;
                string payload = Field(entry, "text") as string;
                if (Equals(Field(entry, "type"), "text") && !string.IsNullOrWhiteSpace(payload) && payload.TrimStart().StartsWith("{", StringComparison.Ordinal))
                    status = Parse(payload);
            }

            var recommended = Field(status, "recommendedDrivers") as object[];
            if (recommended == null)
                return null;

            string desiredChannel = studio ? "STUDIO" : "GAMEREADY";
            string found = null;
            foreach (object item in recommended)
            {
                var driver = item as JsonObject;
                string channel = Field(driver, "channel") as string;
                if (channel == null || channel.Replace("_", "").Replace("-", "").Replace(" ", "")
                    .ToUpperInvariant() != desiredChannel || !Equals(Field(driver, "isUpdateAvailable"), true))
                    continue;

                string version = Field(driver, "version") as string;
                if (version == null || !DriverVersion.IsMatch(version) || (found != null && found != version))
                    return null;

                found = version;
            }
            return found;
        }

        private static JsonObject Parse(string text)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = ResponseLimit, RecursionLimit = 24 };
            return serializer.DeserializeObject(text) as JsonObject;
        }

        private static object Field(JsonObject value, string name)
        {
            object field;
            return value != null && value.TryGetValue(name, out field) ? field : null;
        }

        private sealed class DriverStatusConnection : IDisposable
        {
            private readonly Uri endpoint;
            private readonly HttpClient client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false, UseProxy = false, UseCookies = false
            });
            private string protocol;
            private string session;
            private int nextId = 1;

            public DriverStatusConnection(Uri endpoint)
            {
                this.endpoint = endpoint;
            }

            public async Task<bool> InitializeAsync(CancellationToken token)
            {
                JsonObject result = await SendAsync("initialize", new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new JsonObject(),
                    clientInfo = new { name = "Drivers License", version = typeof(NvidiaAppClient).Assembly.GetName().Version.ToString() }
                }, true, token).ConfigureAwait(false);
                protocol = Field(result, "protocolVersion") as string;
                if (protocol != "2025-11-25" && protocol != "2025-03-26" && protocol != "2024-11-05")
                    return false;

                await SendAsync("notifications/initialized", null, false, token).ConfigureAwait(false);
                return true;
            }

            public async Task<bool> AdvertisesToolAsync(CancellationToken token)
            {
                string cursor = null;
                for (int page = 0; page < 4; page++)
                {
                    JsonObject result = await SendAsync("tools/list", cursor == null ? null : new { cursor },
                        true, token).ConfigureAwait(false);
                    var tools = Field(result, "tools") as object[];
                    if (tools == null)
                        return false;

                    foreach (object item in tools)
                    {
                        var tool = item as JsonObject;
                        if (!Equals(Field(tool, "name"), DriverStatusTool))
                            continue;

                        var schema = Field(tool, "inputSchema") as JsonObject;
                        var required = Field(schema, "required") as object[];
                        object readOnly = Field(Field(tool, "annotations") as JsonObject, "readOnlyHint");
                        return Equals(Field(schema, "type"), "object") &&
                            (Field(schema, "required") == null || (required != null && required.Length == 0)) &&
                            (readOnly == null || Equals(readOnly, true));
                    }

                    cursor = Field(result, "nextCursor") as string;
                    if (string.IsNullOrEmpty(cursor))
                        break;
                }
                return false;
            }

            public Task<JsonObject> CallStatusAsync(CancellationToken token)
            {
                return SendAsync("tools/call", new { name = DriverStatusTool, arguments = new JsonObject() }, true, token);
            }

            private async Task<JsonObject> SendAsync(string method, object parameters, bool expectResponse, CancellationToken token)
            {
                int id = nextId++;
                var body = new JsonObject { { "jsonrpc", "2.0" }, { "method", method } };
                if (expectResponse)
                    body.Add("id", id);
                if (parameters != null)
                    body.Add("params", parameters);

                using (HttpRequestMessage request = MakeRequest(HttpMethod.Post))
                {
                    request.Content = new StringContent(new JavaScriptSerializer().Serialize(body), Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        IEnumerable<string> values;
                        if (method == "initialize" && response.Headers.TryGetValues(SessionHeader, out values))
                        {
                            string value = values.SingleOrDefault();
                            if (value == null || value.Length > 512 || value.Any(character => character < '!' || character > '~'))
                                throw new InvalidOperationException("Invalid MCP session ID.");
                            session = value;
                        }

                        return expectResponse ? await ReadResultAsync(response, id, token).ConfigureAwait(false) : null;
                    }
                }
            }

            private HttpRequestMessage MakeRequest(HttpMethod method)
            {
                var request = new HttpRequestMessage(method, endpoint);
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
                if (protocol != null)
                    request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocol);
                if (session != null)
                    request.Headers.TryAddWithoutValidation(SessionHeader, session);
                return request;
            }

            private static async Task<JsonObject> ReadResultAsync(HttpResponseMessage response, int id, CancellationToken token)
            {
                bool eventStream = response.Content.Headers.ContentType != null &&
                    response.Content.Headers.ContentType.MediaType == "text/event-stream";
                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (token.Register(stream.Dispose))
                {
                    var data = new StringBuilder();
                    var line = new StringBuilder();
                    var buffer = new char[2048];
                    int total = 0;
                    int count;
                    while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        total += count;
                        if (total > ResponseLimit)
                            return null;
                        if (!eventStream)
                        {
                            data.Append(buffer, 0, count);
                            continue;
                        }

                        for (int index = 0; index < count; index++)
                        {
                            char character = buffer[index];
                            if (character != '\n')
                            {
                                if (character != '\r')
                                    line.Append(character);
                                continue;
                            }

                            if (line.Length == 0 && data.Length > 0)
                            {
                                JsonObject message = Parse(data.ToString());
                                if (Equals(Field(message, "id"), id))
                                    return Field(message, "result") as JsonObject;
                                data.Clear();
                            }
                            else
                            {
                                string value = line.ToString();
                                if (value.StartsWith("data:", StringComparison.Ordinal))
                                    data.AppendLine(value.Substring(5).TrimStart());
                            }
                            line.Clear();
                        }
                    }

                    JsonObject reply = data.Length > 0 ? Parse(data.ToString()) : null;
                    return Equals(Field(reply, "id"), id) ? Field(reply, "result") as JsonObject : null;
                }
            }

            public async Task CloseSessionAsync()
            {
                if (session == null)
                    return;
                try
                {
                    using (var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(400)))
                    using (HttpRequestMessage request = MakeRequest(HttpMethod.Delete))
                    using (await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false)) { }
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) { }
            }

            public void Dispose()
            {
                client.Dispose();
            }
        }
    }
}
