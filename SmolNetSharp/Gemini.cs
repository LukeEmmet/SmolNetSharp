using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using Serilog;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;


//originally based on Gemini C# library by InvisibleUp
//https://github.com/InvisibleUp/twinpeaks/tree/master/TwinPeaks/Protocols
//but has been enhanced since to accomodate timeouts and some bugs fixed
//also simplified to be a synchronous client not async
namespace SmolNetSharp.Protocols
{
    public struct GeminiResponse : IResponse
    {
        public char codeMajor;
        public char codeMinor;
        public string meta;
        public Uri uri { get; set; }
        public List<byte> pyld { get; set; }
        public string mime { get; set; }
        public string encoding { get; set; }


        public GeminiResponse(List<byte> header,  Uri uri)
        {
            this.codeMajor = (char)header[0];
            this.codeMinor = (char)header[1];

            int metaStart = 2;
            int metaEnd = header.IndexOf((byte)'\n') - 1;
            byte[] metaraw = header.Skip(metaStart).Take(metaEnd).ToArray();
            this.meta = Encoding.UTF8.GetString(metaraw.ToArray()).TrimStart();

            pyld = new List<byte>();
            this.mime = "text/gemini";      //as default, may be overridden later when we interpret the meta
            this.encoding = "UTF-8";
            this.uri = uri;

        }

        public override string ToString()
        {
            return string.Format(
                "{0}{1}: {2}",
                codeMajor, codeMinor, meta 
            );
        }
    }


    // Significant portions of this code taken from
    // https://docs.microsoft.com/en-us/dotnet/api/system.net.security.sslstream
    public class Gemini 
    {
        private static Hashtable certificateErrors = new Hashtable();
        const int DefaultPort = 1965;



        // The following method is invoked by the RemoteCertificateValidationDelegate.
        // Checks if the server's certificate is valid
        public static bool ValidateServerCertificate(
              object sender,
              X509Certificate certificate,
              X509Chain chain,
              SslPolicyErrors sslPolicyErrors
        ) {
            if (sslPolicyErrors == SslPolicyErrors.None) { return true; }
            // Give a warning on self-signed certs, I guess
            if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors) {
                Log.Warning("Remote server is using self-signed certificate");
                return true;
            }

            // Do not allow this client to communicate with unauthenticated servers.
            Log.Error("Certificate error: {0}", sslPolicyErrors);
            return false;
        }

        static GeminiResponse ReadMessage(SslStream sslStream, Uri uri, int maxSize)
        {
            // Read the  message sent by the server.
            // The end of the message is signaled using the
            // "<EOF>" marker.
            byte[] buffer = new byte[2048];
            int bytes = -1;

            GeminiResponse resp = new GeminiResponse();


            //reader the stream character by character until we get all the header
            //since we can't assume the first chunk would get all the header
            List<byte> header = new List<byte>();
            var byteRead = sslStream.Read(buffer, 0, 1);
            while (byteRead != 0)
            {
                var character = buffer[0];
                header.Add(character);
                if (character == (byte)'\n')        //**TBD should really check for \r\n
                {
                    break;    
                }
                byteRead = sslStream.Read(buffer, 0, 1);
            }

            
            if (header.Count <= 0)
            {
                throw new Exception(
                    string.Format("Invalid Gemini protocol response - missing header")
                );
            }

            resp = new GeminiResponse(header.ToList(), uri);  
            

            //now read the rest of the stream, chunk by chunk.
            bytes = sslStream.Read(buffer, 0, buffer.Length);

            var maxSizeBytes = maxSize * 1024;      //Kb to Bytes

            while (bytes != 0) {
                resp.pyld.AddRange(buffer.Take(bytes));
                bytes = sslStream.Read(buffer, 0, buffer.Length);

                if (resp.pyld.Count > maxSizeBytes)
                {
                    throw new Exception("Abort due to resource exceeding max size (" + maxSize + "Kb)");
                }
            }

            return resp;
        }

        //default of 2Mb, 5 seconds 
        public static IResponse Fetch(Uri hostURL, int abandonReadSizeKb = 2048, int abandonReadTimeS = 5)
        {
            int refetchCount = 0;
        Refetch:
            // Stop unbounded redirects
            if (refetchCount >= 5) {

                throw new Exception(
                    string.Format("Too many redirects!")
                );


            }
            refetchCount += 1;

            // Set remote port
            int port = hostURL.Port;
            if (port == -1) { port = DefaultPort; }

            // Create a TCP/IP client socket.
            // machineName is the host running the server application.
            TcpClient client;
            try {
                client = new TcpClient(hostURL.Host, port);
            } catch (Exception e) {
                Log.Error(e, "Connection failure");
                throw e;
            }

            // Create an SSL stream that will close the client's stream.
            SslStream sslStream = new SslStream(
                client.GetStream(),
                false,
                new RemoteCertificateValidationCallback(ValidateServerCertificate),
                null
            );

            // The server name must match the name on the server certificate.
            try {
                sslStream.AuthenticateAsClient(hostURL.Host);
            } catch (AuthenticationException e) {
                Log.Error(e, "Authentication failure");
                client.Close();
                throw e;
            }

            // Gemini request format: URI\r\n
            byte[] messsage = Encoding.UTF8.GetBytes(hostURL.ToString() + "\r\n");

            GeminiResponse resp = new GeminiResponse();
            try
            {
                sslStream.ReadTimeout = abandonReadTimeS * 1000;     //seconds to MS

                sslStream.Write(messsage, 0, messsage.Count());
                sslStream.Flush();
                // Read message from the server.
                resp = ReadMessage(sslStream, hostURL, abandonReadSizeKb);
                
                
                // Close the client connection.
                //client.Close();
            }
            catch (Exception err)
            {
                sslStream.Close();
                client.Close();
                throw new Exception(err.Message);
                
            }
            finally {
                sslStream.Close();
                client.Close();
            }


            // Determine what to do w/ that
            switch (resp.codeMajor) {
                case '1': // Text input
                    break;
                case '2': // OK
                    break;
                case '3': // Redirect
                    hostURL = new Uri(resp.meta);
                    goto Refetch;

                case '4': // Temporary failure
                case '5': // Permanent failure
                case '6': // Client cert required
                    Log.Error(resp.ToString());
                    resp.pyld = Encoding.UTF8.GetBytes(resp.ToString()).ToList();
                    break;

                default:
                    throw new Exception(
                        string.Format("Invalid response code {0}", resp.codeMajor)
                    );
            }

            return resp;
        }
    }
}
