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


//derived from Gemini.cs
namespace SmolNetSharp.Protocols
{
    public class NimigemRequest
    {
        public string mime;
        public byte[] bytes;

        public NimigemRequest()
        {
            mime = "text/plain";
            bytes = Encoding.ASCII.GetBytes("");
        }
    }

    public struct NimigemResponse : IResponse
    {
        public char codeMajor;
        public char codeMinor;
        public string meta;
        public Uri uri { get; set; }
        public List<byte> bytes { get; set; }
        public string mime { get; set; }
        public string encoding { get; set; }


        public NimigemResponse(Stream responseStream, Uri uri)
        {
            byte[] statusText = { (byte)'4', (byte)'1' };
            var statusBytes = responseStream.Read(statusText, 0, 2);
            if (statusBytes != 2)
            {
                throw new Exception("malformed Nimigem response - no status");
            }

            var status = Encoding.UTF8.GetChars(statusText);
            codeMajor = status[0];
            codeMinor = status[1];


            byte[] space = { 0 };
            var spaceBytes = responseStream.Read(space, 0, 1);
            if (spaceBytes != 1 || space[0] != (byte)' ')
            {
                throw new Exception("malformed Nimigem header - missing space after status");
            }

            List<byte> metaBuffer = new List<byte>();
            byte[] tempMetaBuffer = { 0 };
            byte currentChar;
            while (responseStream.Read(tempMetaBuffer, 0, 1) == 1) {
                currentChar = tempMetaBuffer[0];

                //to debug raw content
                //Console.WriteLine("byte: " + (int)currentChar + ": {" + Encoding.UTF8.GetString(tempMetaBuffer) + "}");

                if (currentChar == (byte)'\r')
                {
                    //read next - should be \n
                    responseStream.Read(tempMetaBuffer, 0, 1);
                    currentChar = tempMetaBuffer[0];

                    //Console.WriteLine("byte: " + (int)currentChar + ": {" + Encoding.UTF8.GetString(tempMetaBuffer) + "}");

                    if (currentChar != (byte) '\n')
                    {
                        throw new Exception("malformed Nimigem header - missing LF after CR");
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
    public class Nimigem
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
                //Log.Warning("Remote server is using self-signed certificate");
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


        static NimigemResponse ReadMessage(SslStream sslStream, Uri uri, int maxSize, int abandonAfterSeconds)
        {
            // Read the  message sent by the server.
            // The end of the message is signaled using the
            // "<EOF>" marker.
            byte[] buffer = new byte[2048];
            int bytes = -1;

            var abandonTime = DateTime.Now.AddSeconds((double)abandonAfterSeconds);
            //initialise and get the codes etc
            NimigemResponse resp = new NimigemResponse(sslStream, uri);


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
        public static IResponse Fetch(Uri hostURL, byte[] payload, string mime = "text/plain; charset=utf-8", X509Certificate2 clientCertificate = null,  string proxy = "", bool insecure = false, int abandonReadSizeKb = 2048, int abandonReadTimeS = 5)
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


            try
            {
                if (clientCertificate == null)
                {
                    sslStream.AuthenticateAsClient(serverHost);

                }
                else
                {
                    var certs = new X509CertificateCollection();
                    certs.Add(clientCertificate);
                    sslStream.AuthenticateAsClient(serverHost, certs, true);
                }

            }
            catch (AuthenticationException e)
            {
                //Log.Error(e, "Authentication failure");
                client.Close();
                throw e;
            }
            

            // Nimigem request format: URI\r\n<datauri>\r\n
            byte[] header = Encoding.UTF8.GetBytes(hostURL.AbsoluteUri + "\r\n");
            byte[] message;

            mime = mime.Replace(" ", "");       //normalise media type for URI
            var encoded = "data:" + mime + ";base64," + Convert.ToBase64String(payload, Base64FormattingOptions.None) + "\r\n";
            var payloadBytes = Encoding.UTF8.GetBytes(encoded);

            message = header.Concat(payloadBytes).ToArray();
            
            NimigemResponse resp = new NimigemResponse();
            try
            {
                sslStream.ReadTimeout = abandonReadTimeS * 1000;    //sslStream timeout is in MS

                sslStream.Write(message, 0, message.Count());
                sslStream.Flush();
                // Read message from the server.
                resp = ReadMessage(sslStream, hostURL, abandonReadSizeKb, abandonReadTimeS);
                
                
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

                sslStream.Dispose();
                client.Dispose();
            }


            // Determine what to do w/ that
            switch (resp.codeMajor) {
                case '1': // Text input

                    //consider this invalid nimigem for now
                    throw new Exception(
                        string.Format("Invalid Nimigem 1X response code {0}{1}", resp.codeMajor, resp.codeMinor)
                    );

                case '2': // OK

                    switch (resp.codeMinor)
                    {
                        case '5':
                            //this is a nimigem extension - we expect to provide the URI on the meta
                            //N.b. This is not the same as 3X redirect, which would trigger another request
                            //instead this indicates success, but nimigem indicates URL as destination
                            //of created asset or results page. this is a separate gemini
                            //URL
                            var successUri = new Uri(resp.meta);
                            if (successUri.Scheme != "gemini")
                            {
                                //invalid meta - target must be gemini
                                throw new Exception(
                                    string.Format("Invalid Nimigem success, not a gemini target: {0}", resp.meta)
                                );
                            }

                            //otherwise OK
                            break;

                        case '0':
                        default:
                            //invalid - success must provide gemini targed response for results
                            throw new Exception(
                                string.Format("Invalid Nimigem 2X response code {0}{1}", resp.codeMajor, resp.codeMinor)
                            );
                    }

                    resp.mime = resp.meta;      //set the mime as the meta response
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
                    
                    //pass back to the client to provide one
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
