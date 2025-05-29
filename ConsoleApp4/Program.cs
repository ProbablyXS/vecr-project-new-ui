using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;

class Program
{
    static void Main()
    {
        int startPort = 8080;
        int endPort = 8090;
        int port = FindFreePort(startPort, endPort);

        if (port == -1)
        {
            Console.WriteLine("Aucun port libre trouvé !");
            return;
        }

        try
        {
            Thread listenerThread = new Thread(() => HandleRequests(port));
            listenerThread.Start();

            Console.WriteLine($"Serveur démarré, écoute sur http://localhost:{port}/");

            OpenBrowser($"http://localhost:{port}/");

            while (true)
            {
                Thread.Sleep(1000);
            }
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"Erreur HttpListener : {ex.Message}");
        }
    }

    static int FindFreePort(int startPort, int endPort)
    {
        for (int port = startPort; port <= endPort; port++)
        {
            try
            {
                HttpListener testListener = new HttpListener();
                testListener.Prefixes.Add($"http://+:{port}/");
                testListener.Start();
                testListener.Stop();
                return port;
            }
            catch (HttpListenerException)
            {
                continue;
            }
        }
        return -1;
    }

    static List<WebSocket> webSockets = new List<WebSocket>();

    static async Task HandleRequests(int port)
    {
        try
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{port}/");
            listener.Start();

            while (true)
            {
                HttpListenerContext context = await listener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(null);
                    WebSocket webSocket = wsContext.WebSocket;

                    lock (webSockets)
                    {
                        webSockets.Add(webSocket);
                    }

                    Console.WriteLine("New client WebSocket connected.");

                    _ = ReceiveWebSocketMessages(webSocket); // Gérer la réception en tâche de fond
                    continue;
                }

                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                Console.WriteLine($"Requête reçue : {request.Url.AbsolutePath} avec méthode {request.HttpMethod}");

                string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string requestedPath = request.Url.AbsolutePath.TrimStart('/');
                string fullPath = Path.Combine(exeDirectory + "/menu", requestedPath);

                // Gérer /getconfig GET
                if (request.Url.AbsolutePath == "/getconfig" && request.HttpMethod == "GET")
                {
                    string iniFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles/default/config.ini");

                    if (File.Exists(iniFilePath))
                    {
                        var iniDict = new Dictionary<string, string>();
                        foreach (var line in File.ReadLines(iniFilePath))
                        {
                            string trimmed = line.Trim();
                            if (!string.IsNullOrWhiteSpace(trimmed) && trimmed.Contains('=') && !trimmed.StartsWith("["))
                            {
                                var parts = trimmed.Split(new[] { '=' }, 2);
                                if (parts.Length == 2)
                                {
                                    iniDict[parts[0].Trim()] = parts[1].Trim();
                                }
                            }
                        }

                        string json = System.Text.Json.JsonSerializer.Serialize(iniDict);
                        byte[] buffer = Encoding.UTF8.GetBytes(json);
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        byte[] buffer = Encoding.UTF8.GetBytes("{\"error\":\"INI file not found\"}");
                        response.ContentType = "application/json";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }

                    response.OutputStream.Close();
                    continue;
                }

                // Gérer /configdata GET
                if (request.Url.AbsolutePath == "/configdata" && request.HttpMethod == "GET")
                {
                    string iniFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles/default/config.ini");
                    var dict = new Dictionary<string, string>();

                    foreach (var line in File.ReadLines(iniFilePath))
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("[") || !trimmed.Contains("=")) continue;
                        var parts = trimmed.Split(new char[] { '=' }, 2);
                        dict[parts[0].Trim()] = parts[1].Trim();
                    }

                    string json = System.Text.Json.JsonSerializer.Serialize(dict);
                    byte[] buffer = Encoding.UTF8.GetBytes(json);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                    continue;
                }

                // Gérer /updateconfig POST
                if (request.Url.AbsolutePath == "/updateconfig" && request.HttpMethod == "POST")
                {
                    string iniFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles/default/config.ini");

                    using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    {
                        string body = await reader.ReadToEndAsync();
                        var updateData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(body);

                        if (updateData != null && updateData.TryGetValue("key", out string key) && updateData.TryGetValue("value", out string value))
                        {
                            var lines = File.ReadAllLines(iniFilePath).ToList();
                            bool found = false;

                            for (int i = 0; i < lines.Count; i++)
                            {
                                if (lines[i].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                                {
                                    lines[i] = $"{key}={value}";
                                    found = true;
                                    break;
                                }
                            }

                            if (!found)
                            {
                                lines.Add($"{key}={value}");
                            }

                            WriteAllLinesWithoutBOM(iniFilePath, lines);

                            byte[] responseBytes = Encoding.UTF8.GetBytes("{\"success\":true}");
                            response.ContentType = "application/json";
                            await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);

                            // Broadcast update to all WebSocket clients
                            await BroadcastWebSocketMessage("{\"type\":\"configUpdated\"}");
                        }
                        else
                        {
                            byte[] error = Encoding.UTF8.GetBytes("{\"error\":\"invalid payload\"}");
                            response.ContentType = "application/json";
                            await response.OutputStream.WriteAsync(error, 0, error.Length);
                        }
                    }

                    response.OutputStream.Close();
                    continue;
                }

                // Servir fichiers statiques
                if (File.Exists(fullPath))
                {
                    string extension = Path.GetExtension(fullPath).ToLower();
                    string contentType;
                    switch (extension)
                    {
                        case ".html": contentType = "text/html"; break;
                        case ".css": contentType = "text/css"; break;
                        case ".js": contentType = "application/javascript"; break;
                        case ".png": contentType = "image/png"; break;
                        case ".jpg":
                        case ".jpeg": contentType = "image/jpeg"; break;
                        case ".gif": contentType = "image/gif"; break;
                        default: contentType = "application/octet-stream"; break;
                    }

                    byte[] fileBytes = File.ReadAllBytes(fullPath);
                    response.ContentLength64 = fileBytes.Length;
                    response.ContentType = contentType;
                    await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                    response.OutputStream.Close();
                    continue;
                }
                else
                {
                    string htmlFilePath = Path.Combine(exeDirectory, "menu/index.html");
                    if (File.Exists(htmlFilePath))
                    {
                        string html = File.ReadAllText(htmlFilePath);
                        byte[] buffer = Encoding.UTF8.GetBytes(html);
                        response.ContentLength64 = buffer.Length;
                        response.ContentType = "text/html; charset=utf-8";
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else
                    {
                        byte[] buffer = Encoding.UTF8.GetBytes("Fichier HTML introuvable.");
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    response.OutputStream.Close();
                }
            }
        }
        catch (HttpListenerException ex)
        {
            Console.WriteLine($"Erreur dans le traitement de la requête : {ex.Message}");
        }
    }

    // Méthode pour broadcast WebSocket
    static async Task BroadcastWebSocketMessage(string message)
    {
        byte[] messageBuffer = Encoding.UTF8.GetBytes(message);
        List<WebSocket> closedSockets = new List<WebSocket>();

        lock (webSockets)
        {
            foreach (var ws in webSockets.ToList())
            {
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        ws.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                    }
                    catch
                    {
                        closedSockets.Add(ws);
                    }
                }
                else
                {
                    closedSockets.Add(ws);
                }
            }

            foreach (var closed in closedSockets)
            {
                webSockets.Remove(closed);
                closed.Dispose();
            }
        }
    }

    // Méthode pour gérer la réception (garder la connexion active)
    static async Task ReceiveWebSocketMessages(WebSocket ws)
    {
        var buffer = new byte[1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    lock (webSockets)
                    {
                        webSockets.Remove(ws);
                    }
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    ws.Dispose();
                }
            }
        }
        catch
        {
            lock (webSockets)
            {
                webSockets.Remove(ws);
            }
            ws.Dispose();
        }
    }

    static void WriteAllLinesWithoutBOM(string path, IEnumerable<string> lines)
    {
        var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using (var writer = new StreamWriter(path, false, utf8NoBom))
        {
            foreach (var line in lines)
            {
                writer.WriteLine(line);
            }
        }
    }

    static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Erreur lors de l'ouverture du navigateur : {ex.Message}");
        }
    }
}