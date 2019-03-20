using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UAFluke8846A
{
    class Program
    {

        public static string SendBuffer(TcpClient tcpClient, byte[] buffer, TimeSpan writeReadTimeout, bool skipResponse = false)
        {
            String result = String.Empty;
            try
            {
                // Get the stream to the client
                NetworkStream stream = tcpClient.GetStream();

                // if not waiting for a response return 
                if (writeReadTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(writeReadTimeout), "Invalid timeout value: zero");

                // Purge residual data in stream
                {
                    int totalThrowaway = 0;
                    while (tcpClient.Available > 0)
                    {
                        byte[] throwAway = new byte[tcpClient.Available];
                        totalThrowaway += stream.Read(throwAway, 0, throwAway.Length);
                    }
                    if (totalThrowaway > 0)
                    {
                        Trace.TraceError("{0}: read and discarded {1} bytes of unexpected data", nameof(SendBuffer), totalThrowaway);
                    }
                }

                stream.WriteTimeout = (int)writeReadTimeout.TotalMilliseconds;
                // Send the message to the connected TcpServer. 
                stream.Write(buffer, 0, buffer.Length);
                if (skipResponse)
                {
                    if (tcpClient.Available > 0) throw new Exception("Unxpected response to command");
                    return string.Empty;
                }

                stream.ReadTimeout = (int)writeReadTimeout.TotalMilliseconds;
                // Allow at least 100 ms for response
                DateTime deadline = DateTime.Now + writeReadTimeout + TimeSpan.FromMilliseconds(100);
                // Receive the TcpServer.response.
                // Buffer to store the response bytes.
                byte[] response = new byte[1024];
                do
                {
                    int available = tcpClient.Available;
                    if (available > response.Length) available = response.Length;
                    if (available > 0)
                    {
                        // Read the first batch of the TcpServer response bytes
                        int responseLen = responseLen = stream.Read(response, 0, available);
                        result += Encoding.ASCII.GetString(response, 0, responseLen);
                    }
                    if (result.Contains('\n'))
                    {
                        return result;
                    }
                    Thread.Sleep(1);
                } while (DateTime.Now < deadline);
                throw new TimeoutException("Failed to receive response before timeout");
            }
            catch (Exception e)
            {
                if (e.InnerException is SocketException)
                {
                    SocketException x = e.InnerException as SocketException;
                    if (x.SocketErrorCode == SocketError.TimedOut)
                    {
                        throw new TimeoutException("Timed out while waiting for response");
                    };
                }
                throw;
            }
        }


        static void Command(TcpClient client, string command, TimeSpan writeReadTimeout)
        {
            Debug.Assert(!command.Contains('\n'));
            command = "*SRE 32\n*CLS\n" + command + "\n*OPC?\n";
            byte[] buffer = Encoding.ASCII.GetBytes(command);
            string response = SendBuffer(client, buffer, writeReadTimeout, false);
            if (!"1\r\n".Equals(response))
            {
                throw new Exception("Unexpected response: " + response + " to command: " + command);
            }
        }

        static string Query(TcpClient client, string query, TimeSpan writeReadTimeout)
        {
            Debug.Assert(!query.Contains('\n'));
            Debug.Assert(query.EndsWith("?"));
            query = query + "\n";
            byte[] buffer = Encoding.ASCII.GetBytes(query);
            string response = SendBuffer(client, buffer, writeReadTimeout, false);
            return response;
        }

        static void CheckErrors(TcpClient client)
        {
            string response = Query(client, "SYST:ERR?", TimeSpan.FromSeconds(1));
            if (!"+0,\"No error\"\r\n".Equals(response))
            {
                throw new Exception("Error: " + response);
            }
        }

        static void Main(string[] args)
        {
            using (TcpClient client = new TcpClient("192.168.1.46", 3490))
            {
                while (true)
                {
                    Command(client, "INIT", TimeSpan.FromSeconds(10));
                    CheckErrors(client);
                    string x = Query(client, "FETCH?", TimeSpan.FromSeconds(1));
                    CheckErrors(client);
                    Trace.WriteLine(x);
                }
            }
        }
    }
}
