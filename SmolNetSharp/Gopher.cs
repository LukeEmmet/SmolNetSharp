using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.IO;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

//A simple gopher client that returns the content and mime
//based on Gemini.cs, but simplified for Gopher
//the mime type is based on the gopher selector type and the file extension
//for common file types.
//gopher maps are labelled as application/gopher-menu
namespace SmolNetSharp.Protocols
{
    public struct GopherResponse : IResponse
    {

        public List<byte> pyld { get; set; }
        public string mime { get; set; }
        public string encoding { get; set; }
        public Uri uri { get; set; }

        public GopherResponse(List<byte> buffer, int bytes, Uri uri)
        {

            int pyldStart = 0;
            int pyldLen = bytes - pyldStart;

            byte[] metaraw = buffer.ToArray();
            this.pyld = buffer.Skip(pyldStart).Take(pyldLen).ToList();

            //slightly fake approach - probably better to parse the path
            //and/or the selector
            this.mime = "application/octet-stream";       //may be overwritten later
            this.encoding = "UTF-8";        //maybe use ASCII instead?
            this.uri = uri;
        }

    }

    // Adapted from Gemini.cs and simplified
    public class Gopher
    {
        const int DefaultPort = 70;

        static  GopherResponse ReadMessage(Stream stream, Uri uri)
        {
            // Read the  message sent by the server.
            // The end of the message is signaled using the
            // "<EOF>" marker.
            byte[] buffer = new byte[2048];
            int bytes = -1;

            bytes = stream.Read(buffer, 0, buffer.Length);
            GopherResponse resp = new GopherResponse(buffer.ToList(), bytes, uri);

            while (bytes != 0) {
                bytes =  stream.Read(buffer, 0, buffer.Length);
                resp.pyld.AddRange(buffer.Take(bytes));
            }

            return resp;
        }

        private static string GetMime(Uri uri, string gopherType)
        {
            //returns application/gopher-menu for search and directories
            //otherwise something based on the gophertype and URI
            //as far as possible. Does not sniff the actual content.

            var res = "application/octet-stream";       //default unspecified format
            var ext = Path.GetExtension(uri.AbsolutePath);

            if (ext.Length > 0) {
                ext = ext.Substring(1);     //convert ".jpg" to "jpg" etc
            } else
            {
                ext = "";
            }



            switch (gopherType)
            {
                case "1":
                case "7":
                    res = "application/gopher-menu";
                    break;
                case "0":
                case "3":
                    res = "text/plain";
                    break;
                case "h":
                    res = "text/html";
                    break;
                case "g":
                    res = "image/gif";
                    break;
                case "4":
                    res = "application/mac-binhex4";
                    break;
                case "I":
                case "s":
                case "d":
                case "9":
                    
                    //for these types we look at the extension, if any, as the best guess 
                    //for the most likely mime type for a number of commonly found file extensions
                    switch (ext) {

                        //common image types
                        case "jpg":
                            res = "image/jpeg";
                            break;
                        case "gif":
                        case "png":
                        case "bmp":
                        case "jpeg":
                            res = "image/" + ext;   //for these, the extension is same as sub type
                            break;

                        //common audio types
                        case "mp3":
                            res = "audio/mpeg";
                            break;
                        case "wav":
                        case "ogg":
                        case "flac":
                            res = "audio/" + ext;   //for these, the extension is same as sub type
                            break;

                        //common document types
                        case "pdf":
                            res = "application/pdf";
                            break;
                        case "doc":
                            res = "application/msword";
                            break;
                        case "ps":
                            res = "application/postscript";
                            break;

                        //common binary files
                        case "zip":
                            res = "application/zip";
                            break;

                    }
                    break;

                    //all others will be interpreted as application/octet-stream
                
            }

            return (res);
        }


        public  static IResponse Fetch(Uri hostURL)
        {
            // Set remote port
            int port = hostURL.Port;
            if (port == -1) { port = DefaultPort; }

            // Create a TCP/IP client socket.
            TcpClient client;
            try {
                client = new TcpClient(hostURL.Host, port);
            } catch (Exception e) {
                Log.Error(e, "Connection failure");
                throw e;
            }

            // Create an stream that will close the client's stream.
            Stream stream = client.GetStream();
            
            var gopherType = "1";   //map by default - e.g. for top level domain listings etc
            var trimmedUrl = hostURL.AbsolutePath;      //will include leading / even if there was not one in the original string

            if (hostURL.AbsolutePath.Length > 1)
            {
                gopherType = trimmedUrl[1].ToString();      //e.g. extract "0" from "/0foo" or "/0/bar"

                //remove first two parts
                trimmedUrl = trimmedUrl.Substring(2);
            }

            var usePath = Uri.UnescapeDataString(trimmedUrl);        //we need to unescape any escaped characters like %20 back to space etc

            // Gopher request format: path\r\n
            byte[] messsage = Encoding.UTF8.GetBytes(usePath + "\r\n");
            stream.Write(messsage, 0, messsage.Count());
            stream.Flush();
            // Read message from the server.
            GopherResponse resp = ReadMessage(stream, hostURL);
            // Close the client connection.
            client.Close();

            //infer a suitable mime type from the url and type
            resp.mime = GetMime(hostURL, gopherType);
            
            return resp;
        }
    }
}
