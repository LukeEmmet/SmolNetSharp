using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Linq;


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
        public List<byte> bytes { get; set; }
        public string mime { get; set; }
        public string encoding { get; set; }


        public GeminiResponse(Stream responseStream, Uri uri)
        {
            byte[] statusText = { (byte)'4', (byte)'1' };
            var statusBytes = responseStream.Read(statusText, 0, 2);
            if (statusBytes != 2)
            {
                throw new Exception("malformed Gemini response - no status");
            }

            var status = Encoding.UTF8.GetChars(statusText);
            codeMajor = status[0];
            codeMinor = status[1];


            byte[] space = { 0 };
            var spaceBytes = responseStream.Read(space, 0, 1);
            if (spaceBytes != 1 || space[0] != (byte)' ')
            {
                throw new Exception("malformed Gemini response - missing space after status");
            }

            List<byte> metaBuffer = new List<byte>();
            byte[] tempMetaBuffer = { 0 };
            byte currentChar;
            while (responseStream.Read(tempMetaBuffer, 0, 1) == 1)
            {
                currentChar = tempMetaBuffer[0];

                //to debug raw content
                //Console.WriteLine("byte: " + (int)currentChar + ": {" + Encoding.UTF8.GetString(tempMetaBuffer) + "}");

                if (currentChar == (byte)'\r')
                {
                    //read next - should be \n
                    responseStream.Read(tempMetaBuffer, 0, 1);
                    currentChar = tempMetaBuffer[0];

                    //Console.WriteLine("byte: " + (int)currentChar + ": {" + Encoding.UTF8.GetString(tempMetaBuffer) + "}");

                    if (currentChar != (byte)'\n')
                    {
                        throw new Exception("malformed Gemini header - missing LF after CR");
                    }
                    break;      //no more header processing
                }

                //add to the header buffer
                metaBuffer.Add(currentChar);
            }

            var meta = Encoding.UTF8.GetString(metaBuffer.ToArray());

            this.meta = meta;
            bytes = new List<byte>();
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
            if (sslPolicyErrors == SslPolicyErrors.None) { 
                return true; 
            }

            var expireDate = DateTime.Parse(certificate.GetExpirationDateString());
            if (expireDate < DateTime.Now)
            {
                return false;
            }

            //self signed certificates are considered OK in Gemini
            if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors) {
                return true;
            }

            // Do not allow this client to communicate with unauthenticated servers.
            //Log.Error("Certificate error: {0}", sslPolicyErrors);
            return false;
        }


        public static bool AlwaysAccept(
             object sender,
             X509Certificate certificate,
             X509Chain chain,
             SslPolicyErrors sslPolicyErrors
        )
        {
            return true;
        }


        static GeminiResponse ReadMessage(SslStream sslStream, Uri uri, int maxSize, int abandonAfterSeconds)
        {
            // Read the  message sent by the server.
            // The end of the message is signaled using the
            // "<EOF>" marker.
            byte[] buffer = new byte[2048];
            int bytes = -1;

            var abandonTime = DateTime.Now.AddSeconds((double)abandonAfterSeconds);
            //initialise and get the codes etc
            GeminiResponse resp = new GeminiResponse(sslStream, uri);

            //now read the rest of the stream, chunk by chunk.
            bytes = sslStream.Read(buffer, 0, buffer.Length);

            var maxSizeBytes = maxSize * 1024;      //Kb to Bytes

            while (bytes != 0) {
                resp.bytes.AddRange(buffer.Take(bytes));
                bytes = sslStream.Read(buffer, 0, buffer.Length);

                if (resp.bytes.Count > maxSizeBytes)
                {
                    throw new Exception("Abort due to resource exceeding max size (" + maxSize + "Kb)");
                }

                if (DateTime.Now >= abandonTime)
                {
                    throw new Exception("Abort due to resource exceeding time limit (" + abandonAfterSeconds + " seconds)");
                }
            }

            return resp;
        }


        //default of 2Mb, 5 seconds. proxy string can be empty, meaning connect to host directly 
        public static IResponse Fetch(Uri hostURL, X509Certificate2 clientCertificate = null, string proxy = "", bool insecure = false, int abandonReadSizeKb = 2048, int abandonReadTimeS = 5)
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

            var serverHost = hostURL.Host;

            // Set remote port
            int port = hostURL.Port;
            if (port == -1) { port = DefaultPort; }

            if (proxy.Length > 0)
            {
                var proxySplit = proxy.Split(':');
                serverHost = proxySplit[0];
                port = int.Parse(proxySplit[1]);
            }

            // Create a TCP/IP client socket.
            // machineName is the host running the server application.
            TcpClient client;
            try {
                client = new TcpClient(serverHost, port);
            } catch (Exception e) {
                //Log.Error(e, "Connection failure");
                throw e;
            }

            //by default we validate against the certificate
            RemoteCertificateValidationCallback callback = new RemoteCertificateValidationCallback(ValidateServerCertificate);

            //if explicitly instructed always accespt the certificate
            if (insecure)
            {
                callback = new RemoteCertificateValidationCallback(AlwaysAccept);
            }
          
            // Create an SSL stream that will close the client's stream.
            SslStream sslStream = new SslStream(
                client.GetStream(),
                false,
                callback,
                null
            );

            var certs = new X509CertificateCollection();
            if (clientCertificate != null) { 
                certs.Add(clientCertificate);
            }

            //explicitly set tls version otherwise call will fail on windows 7. Changed from .net 4.61 to 4.7
            //See following discussion
            //https://github.com/dotnet/runtime/issues/23217
            sslStream.AuthenticateAsClient(serverHost, certs, SslProtocols.Tls12, !insecure);

            // Gemini request format: URI\r\n
            byte[] messsage = Encoding.UTF8.GetBytes(hostURL.AbsoluteUri + "\r\n");

            GeminiResponse resp = new GeminiResponse();
            try
            {
                sslStream.ReadTimeout = abandonReadTimeS * 1000;    //sslStream timeout is in MS

                sslStream.Write(messsage, 0, messsage.Count());
                sslStream.Flush();
                // Read message from the server.
                resp = ReadMessage(sslStream, hostURL, abandonReadSizeKb, abandonReadTimeS);
            }
            catch (Exception err)
            {
                sslStream.Close();
                client.Close();
                throw new Exception(err.Message);
                
            }
            finally {
                // Close the client connection.
                sslStream.Close();
                client.Close();

                sslStream.Dispose();
                client.Dispose();
            }


            // Determine what to do w/ that
            switch (resp.codeMajor) {
                case '1': // Text input
                    break;
                case '2': // OK
                    resp.mime = resp.meta;      //set the mime as the meta response **TBD parse this into media type/encoding etc
                    break;
                case '3': // Redirect

                    Uri redirectUri;

                    if (resp.meta.Contains("://"))
                    {
                        //a full url
                        redirectUri = new Uri(resp.meta);
                    }
                    else
                    {
                        redirectUri = new Uri(hostURL, resp.meta);
                    }

                    if (redirectUri.Scheme != hostURL.Scheme)
                    {
                        //invalid meta - target must be same scheme as source
                        throw new Exception("Cannot redirect to a URI with a different scheme: " + redirectUri.Scheme);
                    }

                    hostURL = redirectUri;

                    goto Refetch;

                case '4': // Temporary failure
                case '5': // Permanent failure
                case '6': // Client cert required
                    resp.bytes = Encoding.UTF8.GetBytes(resp.ToString()).ToList();
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
